mod checker;
mod constants;

use std::env;
use std::ffi::CString;
use std::fs;
use std::fs::File;
use std::io::Error as IOError;
use std::io::ErrorKind as IOErrorKind;
use std::io::Seek;
use std::io::SeekFrom;
use std::io::Write;
use std::os::fd::AsRawFd;
use std::os::fd::FromRawFd;
use std::os::fd::OwnedFd;
use std::process::Stdio;
use std::process::exit;

use libc::MFD_CLOEXEC;
use libc::SYS_memfd_create;
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::process::{Child, Command};

use crate::checker::{Stage, parse_checker_config};
use crate::constants::{
    CHECKER_PATH, DEFAULT_CHECKER_GRACE, DEFAULT_USER_TIMEOUT_LIMIT, MAX_OUTPUT_LIMIT_BYTES,
};

#[derive(Debug)]
enum RelayError {
    OutputLimitExceeded,
    Io(IOError),
}

impl From<IOError> for RelayError {
    fn from(err: IOError) -> Self {
        RelayError::Io(err)
    }
}

fn memfd_create(name: &str) -> std::io::Result<OwnedFd> {
    let cname = CString::new(name)
        .map_err(|_| IOError::new(IOErrorKind::InvalidInput, "invalid memfd name"))?;

    let fd = unsafe { libc::syscall(SYS_memfd_create, cname.as_ptr(), MFD_CLOEXEC) as i32 };
    if fd < 0 {
        return Err(IOError::last_os_error());
    }

    Ok(unsafe { OwnedFd::from_raw_fd(fd) })
}

fn kill_pgroup(child: &Child) {
    if let Some(pid) = child.id() {
        // Kill the entire process group by sending a signal to -pid
        // Convert safely to pid_t (i32) before calling kill
        let pid_i32 = if pid <= i32::MAX as u32 {
            pid as i32
        } else {
            return;
        };
        unsafe {
            libc::kill(-(pid_i32), libc::SIGKILL);
        }
    }
}

async fn kill_and_reap(mut child: Child) {
    kill_pgroup(&child);
    let _ = child.kill().await;
    let _ = child.wait().await;
}

fn is_broken_pipe(err: &IOError) -> bool {
    matches!(
        err.kind(),
        IOErrorKind::BrokenPipe | IOErrorKind::ConnectionReset | IOErrorKind::UnexpectedEof
    )
}

fn spawn_checker(
    code: &str,
    args: &[String],
    stage: Stage,
) -> Result<Child, Box<dyn std::error::Error>> {
    let memfd = memfd_create("checker")?;

    let mut file = File::from(memfd);
    file.write_all(code.as_bytes())?;
    file.flush()?;
    file.seek(SeekFrom::Start(0))?; // rewind to the beginning for checker to read

    let fd_num = file.as_raw_fd();
    let path = format!("/proc/self/fd/{fd_num}");

    let mut cmd: Command = Command::new("/usr/bin/python3");
    cmd.arg("-u")
        .arg(path)
        .args(args) // TODO checker args is work
        .env("STAGE", stage.to_string())
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::inherit());

    unsafe {
        cmd.pre_exec(move || {
            // process group isolation
            if libc::setpgid(0, 0) != 0 {
                return Err(IOError::last_os_error());
            }

            // Ensure the memfd is not closed on exec
            let flags = libc::fcntl(fd_num, libc::F_GETFD);
            if flags < 0 {
                return Err(IOError::last_os_error());
            }
            if libc::fcntl(fd_num, libc::F_SETFD, flags & !libc::FD_CLOEXEC) < 0 {
                return Err(IOError::last_os_error());
            }

            Ok(())
        });
    }

    Ok(cmd.spawn()?)
}

fn spawn_user(user_cmd: &str, user_args: &[String]) -> Result<Child, Box<dyn std::error::Error>> {
    let mut cmd = Command::new(user_cmd);

    cmd.args(user_args)
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::inherit());

    unsafe {
        cmd.pre_exec(|| {
            // process group isolation
            if libc::setpgid(0, 0) != 0 {
                return Err(IOError::last_os_error());
            }

            Ok(())
        });
    }

    Ok(cmd.spawn()?)
}

#[tokio::main(flavor = "current_thread")]
async fn main() -> Result<(), Box<dyn std::error::Error>> {
    unsafe {
        libc::signal(libc::SIGPIPE, libc::SIG_IGN);
    }

    let args: Vec<String> = env::args().collect();
    if args.len() < 2 {
        eprintln!("Usage: judge-interactor <user_program> [args...]");
        exit(1);
    }

    let user_cmd = &args[1];
    let user_args = args[2..].to_vec();

    let checker_code = fs::read_to_string(CHECKER_PATH).unwrap_or_default();
    let config = parse_checker_config(&checker_code);

    let user_timeout = config.timeout.unwrap_or(DEFAULT_USER_TIMEOUT_LIMIT);
    let checker_grace = config.checker_grace.unwrap_or(DEFAULT_CHECKER_GRACE);

    // prevent user reading checker
    let _ = fs::remove_file(CHECKER_PATH);

    // PRE
    if config.pre {
        let mut checker = spawn_checker(&checker_code, &user_args, Stage::Pre)?;

        let status = tokio::select! {
            res = checker.wait() => res?,
            _ = tokio::time::sleep(user_timeout) => {
                kill_and_reap(checker).await;
                eprintln!("Pre-checker timeout");
                exit(1);
            }
        };

        if !status.success() {
            eprintln!("Pre-checker failed");
            exit(1);
        }
    }

    // RUN
    if config.run {
        let mut checker = spawn_checker(&checker_code, &user_args, Stage::Run)?;

        let mut checker_stdout = match checker.stdout.take() {
            Some(s) => s,
            None => {
                eprintln!("Checker stdout not piped");
                kill_and_reap(checker).await;
                exit(1);
            }
        };

        let mut checker_stdin = match checker.stdin.take() {
            Some(s) => s,
            None => {
                eprintln!("Checker stdin not piped");
                kill_and_reap(checker).await;
                exit(1);
            }
        };

        // ensure checker is ready to read/write before spawning
        tokio::task::yield_now().await;

        let mut user = spawn_user(user_cmd, &user_args)?;
        let mut user_stdout = match user.stdout.take() {
            Some(s) => s,
            None => {
                eprintln!("User stdout not piped");
                kill_and_reap(checker).await;
                kill_and_reap(user).await;
                exit(1);
            }
        };

        let mut user_stdin = match user.stdin.take() {
            Some(s) => s,
            None => {
                eprintln!("User stdin not piped");
                kill_and_reap(checker).await;
                kill_and_reap(user).await;
                exit(1);
            }
        };

        // Cancellation channel to signal route tasks to stop when user process exits or times out
        let (tx_cancel, mut rx_cancel) = tokio::sync::oneshot::channel::<()>();

        // checker stdout -> user stdin
        let route_checker_to_user = tokio::spawn(async move {
            let res = tokio::io::copy(&mut checker_stdout, &mut user_stdin).await;
            let _ = user_stdin.shutdown().await;
            let _ = tx_cancel.send(()); // signal route_user_to_checker to stop
            res
        });

        //  user stdout -> system stdout + checker stdin
        let route_user_to_checker = tokio::spawn(async move {
            let mut total_output = 0usize;
            let mut buf = [0u8; 8192];
            let mut sys_stdout = tokio::io::stdout();

            loop {
                tokio::select! {
                    _ = &mut rx_cancel => break,
                    res = user_stdout.read(&mut buf) => {
                        let n = match res {
                            Ok(n) => n,
                            Err(e) => {
                                if is_broken_pipe(&e) {
                                    break;
                                }

                                return Err(RelayError::Io(e));
                            }
                        };
                        if n == 0 {
                            break;
                        }

                        total_output += n;
                        if total_output > MAX_OUTPUT_LIMIT_BYTES {
                            return Err(RelayError::OutputLimitExceeded);
                        }

                        if let Err(e) = sys_stdout.write_all(&buf[..n]).await {
                            eprintln!("Stdout write error: {}", e);
                        }
                        if let Err(e) = checker_stdin.write_all(&buf[..n]).await {
                            if is_broken_pipe(&e) { break; }
                            return Err(RelayError::Io(e));
                        }
                    }
                }
            }

            checker_stdin.shutdown().await?;

            Ok::<(), RelayError>(())
        });

        // wait user
        let user_status = tokio::select! {
            res = user.wait() => res?,
            _ = tokio::time::sleep(user_timeout) => {
                kill_and_reap(user).await;
                kill_and_reap(checker).await;

                eprintln!("User timeout");
                exit(1);
            }
        };

        //  wait routes
        let route_a_result = route_checker_to_user.await;
        let route_b_result = route_user_to_checker.await;

        let mut failed = false;
        match route_a_result {
            Ok(Ok(_)) => {}
            _ => {
                eprintln!("Checker to user relay failed");
                failed = true;
            }
        }

        match route_b_result {
            Ok(Ok(())) => {}
            Ok(Err(RelayError::OutputLimitExceeded)) => {
                eprintln!("Output limit exceeded");
                failed = true;
            }
            Ok(Err(RelayError::Io(e))) => {
                eprintln!("Pipe relay failed: {}", e);
                failed = true;
            }
            Err(e) => {
                eprintln!("Relay task join error: {}", e);
                failed = true;
            }
        }

        if failed {
            kill_and_reap(checker).await;
            kill_and_reap(user).await;

            // Already printed error message, just exit here
            exit(1);
        }

        // checker grace
        let checker_status = tokio::select! {
            res = checker.wait() => res?,
            _ = tokio::time::sleep(checker_grace) => {
                kill_and_reap(checker).await;

                eprintln!("Checker grace timeout");
                exit(1);
            }
        };

        if !checker_status.success() {
            eprintln!("Run-checker failed");
            exit(1);
        }

        if !user_status.success() {
            eprintln!("User exited with code {}", user_status.code().unwrap_or(-1));
            exit(user_status.code().unwrap_or(1));
        }
    } else {
        let mut user = spawn_user(user_cmd, &user_args)?;
        let status = tokio::select! {
            res = user.wait() => res?,
            _ = tokio::time::sleep(user_timeout) => {
                kill_and_reap(user).await;

                // eprintln!("User timeout");
                exit(1);
            }
        };

        if !status.success() {
            // eprintln!("User exited with code {}", status.code().unwrap_or(-1));
            exit(status.code().unwrap_or(1));
        }
    }

    // POST
    if config.post {
        let mut checker = spawn_checker(&checker_code, &user_args, Stage::Post)?;
        let status = tokio::select! {
            res = checker.wait() => res?,
            _ = tokio::time::sleep(user_timeout) => {
                kill_and_reap(checker).await;
                eprintln!("Post-checker timeout");
                exit(1);
            }
        };

        if !status.success() {
            eprintln!("Post-checker failed");
            exit(1);
        }
    }

    exit(0);
}

use std::time::Duration;

pub const CHECKER_PATH: &str = ".checker.py";

pub const DEFAULT_USER_TIMEOUT_LIMIT: Duration = Duration::from_secs(5);
pub const DEFAULT_CHECKER_GRACE: Duration = Duration::from_millis(500);

pub const MAX_OUTPUT_LIMIT_BYTES: usize = 32 * 1024 * 1024;

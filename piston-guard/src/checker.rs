use std::fmt;
use std::time::Duration;

#[derive(Debug, Clone, Copy, PartialEq)]
pub enum Stage {
    Pre,
    Run,
    Post,
}

impl fmt::Display for Stage {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Stage::Pre => write!(f, "pre"),
            Stage::Run => write!(f, "run"),
            Stage::Post => write!(f, "post"),
        }
    }
}

#[derive(Debug, Clone, Copy)]
pub struct CheckerConfig {
    pub pre: bool,
    pub run: bool,
    pub post: bool,
    pub timeout: Option<Duration>,
    pub checker_grace: Option<Duration>,
}

impl CheckerConfig {
    pub fn new() -> Self {
        Self {
            pre: false,
            run: false,
            post: false,
            timeout: None,
            checker_grace: None,
        }
    }
}

pub fn parse_timeout(token: &str) -> Option<Duration> {
    let token = token.replace(' ', "");
    if let Some(unit_index) = token.find(|c: char| c.is_alphabetic()) {
        let (num_str, unit_str) = token.split_at(unit_index);
        if let Ok(val) = num_str.parse::<f64>() {
            return match unit_str.to_lowercase().as_str() {
                "ms" => Some(Duration::from_secs_f64(val / 1000.0)),
                "s" => Some(Duration::from_secs_f64(val)),
                "m" | "min" => Some(Duration::from_secs_f64(val * 60.0)),
                _ => None,
            };
        }
    } else if let Ok(val) = token.parse::<f64>() {
        return Some(Duration::from_secs_f64(val));
    }

    None
}

pub fn extract_tag_value<'a>(text: &'a str, tag: &str) -> Option<&'a str> {
    let text_lower = text.to_lowercase();
    let tag_lower = tag.to_lowercase();

    let pos = text_lower.find(&tag_lower)?;
    let remain = &text[pos + tag.len()..];
    let end = remain.find(']')?;

    Some(&remain[..end])
}

pub fn parse_checker_config(code: &str) -> CheckerConfig {
    let mut config = CheckerConfig::new();

    if code.trim().is_empty() {
        return config;
    }

    let Some(first_line) = code.lines().next() else {
        return config;
    };

    let trimmed = first_line.trim_start();
    if !trimmed.starts_with('#') {
        config.post = true;
        return config;
    }

    let normalized = trimmed.to_lowercase();
    if normalized.contains("[pre]") {
        config.pre = true;
    }
    if normalized.contains("[run]") {
        config.run = true;
    }
    if normalized.contains("[post]") {
        config.post = true;
    }

    if let Some(v) = extract_tag_value(trimmed, "[timeout=") {
        config.timeout = parse_timeout(v);
    }
    if let Some(v) = extract_tag_value(trimmed, "[checker_grace=") {
        config.checker_grace = parse_timeout(v);
    }

    if !config.pre && !config.run && !config.post {
        config.post = true;
    }

    config
}

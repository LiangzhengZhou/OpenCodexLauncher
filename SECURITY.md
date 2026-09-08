# Security and privacy / 安全与隐私

Do not publish settings, credential stores, logs, recovery journals, upstream configuration or screenshots containing account information. The release allowlist is separate from .gitignore; distribute generated release directories, not a development folder.

Launcher-saved credentials use Windows DPAPI scoped to the current user. Settings and backups are not an encrypted vault; upstream files may contain plaintext credentials. Private recovery journals include original paths and original file contents. Keep them on a trusted local account and exclude them from public backups.

Fresh onboarding does not read ambient providers or credentials. Detection is explicit with preview; manual setup uses empty homes. Tests inject isolated homes and dummy keys. Never add tests accessing live account files or paid endpoints.

Route evidence is session-associated and timestamped. Missing evidence means pending. Reserve Force requires confirmation and supported source structure, journals owned writes, and refuses detected external edits during recovery. This is not a filesystem-wide lock against third-party applications; avoid editing upstream files during the operation.

Report vulnerabilities through the repository host's private reporting facility if enabled. Otherwise ask the maintainer for a private contact channel without posting exploit details or credentials in a public issue. If a real key was published, revoke it at the provider and remove it from repository history and release assets; deleting the latest file alone is insufficient.

请勿公开个人设置、凭据、日志、恢复记录及含账号信息的截图。DPAPI 只保护启动器凭据库；上游配置和恢复副本可能含明文密钥。发现真实密钥泄露后应在供应商处撤销，并清理仓库历史和发布资产。安全报告请使用私密渠道，不要把密钥粘贴到公开 issue。

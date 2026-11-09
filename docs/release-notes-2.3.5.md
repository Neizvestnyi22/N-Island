## N-Island 2.3.5 — updates no longer fail on “504 Gateway Time-out”

*made in TG [@Ne1zvestny1i](https://t.me/Ne1zvestny1i) & Claude*

Update from the app: **Settings → About → Update** · Обновление прямо из приложения: **Настройки → О программе → Обновить**.

- **Retries and a fallback address:** GitHub's direct release download link sometimes answers 504 (it did right after 2.3.4 came out), and the updater gave up at once. Now it makes 2 attempts on the direct link, then downloads the same file through the GitHub API (up to 4 attempts with 2–10 s pauses), resumes from where a download broke off and drops a download that stalls for 60 s. The SHA-256 from SHA256SUMS.txt is still checked, and files still come only from GitHub.
- **Readable errors:** instead of “net_http_message_not_success_statuscode_reason, 504, Gateway Time-out” you get “The GitHub download server did not answer in time (504) — try again in a minute”, “No connection to GitHub — check the internet (or VPN)” and so on.
- `--selftest update` downloads the latest release file both ways and checks its SHA-256 without replacing the program.

Обновление больше не срывается на 504: повторы, запасной адрес через API GitHub, докачка и понятные сообщения об ошибках.

**If 2.3.4 or older can't update because of 504:** those versions don't have the fallback yet — try again later, or download the file from this page.

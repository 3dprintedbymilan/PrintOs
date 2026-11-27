# Spike: Bambu Lab connectivity (#48)

Time-boxed research for the owner's printer, a Bambu Lab A1. The implementation of the cloud connection already exists (#78, see [bambu.md](bambu.md)); this document records what we know about the connection options, what the existing implementation proved, and what to do next.

Status labels: **verified** = confirmed in at least one primary or well-maintained community source read during this spike; **secondary** = single non-official source, treat as likely; **unverified** = not confirmed, or only testable with real hardware.

## 1. Connection methods

| Method | Reachable from Azure | Needs | Status |
| --- | --- | --- | --- |
| Bambu Cloud MQTT (`us.mqtt.bambulab.com:8883`, TLS) | Yes | Account access token; user name `u_<uid>` | Implemented in PrintOS; endpoint and auth scheme verified in OpenBambuAPI, never run against the real cloud in this project |
| Bambu Cloud REST (`api.bambulab.com/v1`) | Yes | Bearer access token | Implemented (account, bound devices, tasks); verified in OpenBambuAPI |
| LAN mode MQTT (`<printer ip>:8883`, TLS) | No, only from the same network | User `bblp`, password is the printer's LAN access code; LAN Only mode or Developer mode | Documented, not implemented |
| Bambu Connect / official third-party route | Not applicable (desktop software on the same network) | Bambu Connect app | Not usable from a cloud backend; see below |
| Camera: RTSPS `rtsps://<ip>:322/streaming/live/1` | No | LAN access code (`bblp`) | Not available on A1/P1 (below) |
| Camera: proprietary TCP/JPEG stream `<ip>:6000` | No | LAN access code | A1/P1 only, LAN only |

### 1.1 Cloud (primary, implemented)

- REST login is `POST /v1/user-service/user/login` with a password or an e-mail verification code; the response carries `accessToken` and `refreshToken` (OpenBambuAPI `cloud-http.md`).
- PrintOS does not log in. The owner pastes the `token` cookie from bambulab.com, which avoids ever handling the password or verification code.
- Tasks: `GET /v1/user-service/my/tasks` (status 2 finished, 3 aborted or failed, weights and `costTime` are slicer estimates, use `endTime - startTime` for real duration). Devices: `GET /v1/iot-service/api/user/bind`. Account id: `GET /v1/design-user-service/my/preference`.
- MQTT: topics `device/<serial>/report` (printer to client) and `device/<serial>/request`. The only message PrintOS publishes is the read-only `pushing.pushall` request. Full status objects: X1 series sends the whole object each update, P1 series (and in practice the A1) sends deltas, hence `pushall` once after connecting. OpenBambuAPI warns against calling `pushall` more often than every 5 minutes on the P1P because it can lag the printer; PrintOS calls it once per connection.
- Token lifetime: OpenBambuAPI reports `expiresIn` of 7 776 000 s (about 3 months) and says the refresh endpoint now only returns 401, so refreshing is effectively unavailable. This means the owner re-pastes a token roughly every 3 months (**verified** in community docs, not by Bambu).

### 1.2 LAN mode (not usable from Azure)

- MQTT on the printer, port 8883 with TLS, user `bblp`, password = access code shown on the printer screen (**verified**, OpenBambuAPI `mqtt.md`).
- The printer must be reachable on the local network. PrintOS runs on Azure Container Apps and cannot reach a home network without a VPN or an agent.
- LAN Only mode disconnects the printer from the Bambu cloud (no Bambu Handy remote monitoring, no cloud slicing hand-off). Developer mode is an add-on to LAN Only mode that keeps MQTT, the live stream and FTP open without the authorization check; Bambu's wiki states it cannot be combined with cloud connection (**verified**, Bambu wiki via search summary; the wiki page itself could not be fetched during this spike).
- Developer mode on the A1 needs a recent firmware (a third-party help page names A1 01.05.00.00 or later: **secondary**, check Bambu's release notes).
- Bambu says it will not support integrations built on these protocols (**verified**, Bambu blog post, 20 January 2025 and press coverage).
- Consequence: choosing LAN for PrintOS means giving up the cloud for that printer. That conflicts with the "works anywhere, from the phone" product promise.

### 1.3 Official routes and their status

- Bambu announced an authorization control system (Bambu blog, 20 January 2025): printer commands from third-party software pass through authorized channels, with **Bambu Connect** as the official bridge on the local network and **Developer mode** as the opt-out. Bambu states the aim is not to block third-party software and mentions a dedicated route for print-farm software (contact devpartner@bambulab.com). Source: https://blog.bambulab.com/updates-and-third-party-integration-with-bambu-connect (**verified**).
- There is no public, documented, supported cloud API for third parties. What PrintOS uses is the same API the Bambu apps use, documented by the community (OpenBambuAPI).
- Cloud policy statement of 12 November 2026, reported by a news site (**secondary**, original forum post not read): third-party software may keep using non-critical cloud status queries (temperatures, progress) as long as usage stays moderate; assigning printers to an account, direct live video and direct firmware updates are not intended for third parties; print start is only meant via Bambu Connect; Bambu may limit or suspend access when load is a risk. This followed a traffic spike that Bambu blocked, attributed (unconfirmed) to a Home Assistant integration bug fixed on 11 November 2026. Source: https://www.tutkit.com/en/news/1081-bambu-lab-cloud-blocking-third-party-software.
- Reading status and finished tasks (what PrintOS does) falls in the allowed category per that statement. Sending commands (not implemented, not planned) would not.

### 1.4 Camera

| Model | Stream | Needs |
| --- | --- | --- |
| X1 series, P2S | RTSPS `:322/streaming/live/1` | LAN access code, on by default |
| H2 series | same URL, off by default (a "LAN mode liveview" toggle) | access code |
| A1, P1 | proprietary TCP server `:6000`, 1280x720 JPEG frames after a 64-byte auth packet | access code (`bblp`) |

Source: OpenBambuAPI `video.md` (**verified**). So RTSP on port 322 does not exist on the A1; it has the proprietary JPEG stream, and all of these are LAN only. The cloud path (TUTK / Agora P2P relays used by the Bambu apps) is proprietary and, per the cloud policy above, not intended for third parties. There is no honest way to show a live camera from Azure. A bridge on the home network (section 4) is the only route.

## 2. Limits and risks

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Unofficial API | Endpoints, hosts or payloads can change or be blocked without notice | All Bambu code behind `IBambuCloudClient` / `IBambuTelemetrySource`; failures degrade to "offline / token expired" screens, never to data loss |
| Token expiry (about 3 months, no refresh) | Live status and sync stop until the owner pastes a new token | Clear "token expired" state, one-step reconnect; notification when the notifications work lands |
| Rate limits and blocking | Bambu may throttle or block abusive clients (Nov 2026 incident, secondary source) | One MQTT subscription per printer, `pushall` once per connection, REST sync every few minutes (`Bambu:SyncMinutes`), exponential backoff 5 s to 5 min, no polling loops |
| Terms of service | Using the cookie token and community-documented API is not an agreed integration; Bambu could treat it as a violation | Read-only, low volume, user-supplied token, own account only, no password handling. Not legal advice; the owner accepts the risk. Not offered as a multi-tenant feature without a written agreement with Bambu |
| Token is an account credential | Leak gives access to the Bambu account | Encrypted with Data Protection, never returned or logged, not audited, excluded from account export |
| Delta reports | A client that connects mid-print sees partial data | `pushall` on connect; stored state merges deltas |
| Weights are estimates | Filament consumption from cloud tasks is not measured | Only booked when unambiguous, otherwise flagged (see bambu.md) |
| Regional hosts | China hosts unverified | Overridable in configuration |

## 3. What the proof of concept proved

The merged implementation (module `Bambu`), fakes (`FakeBambuCloudClient`, `FakeBambuTelemetrySource`) and sample payloads (`BambuReportParserTests`) show, in automated tests with Postgres:

- A token can be stored encrypted, validated through the client interface and re-validated, with expiry surfaced as a state.
- A telemetry source can feed `report` payloads (full and delta, string and number encodings, AMS trays, external spool) into a live status per printer, with backoff and offline marking on disconnect.
- Cloud tasks can be turned into idempotent print jobs and filament consumption through the normal print job rules.
- Workspace isolation and no secret leakage (audit log, responses) hold.
- The MQTT client (MQTTnet, TLS, `u_<uid>` credentials) and the HTTP client compile and follow the documented protocol.

## 4. What it did not prove (needs the real A1 / a real account)

- That the real cloud accepts the pasted cookie token for REST and MQTT today (the code follows OpenBambuAPI; never run live from this project).
- The exact shape of the A1's real `report` messages, firmware differences, the frequency of delta messages, and whether `pushall` returns the AMS block as assumed.
- The `gcode_state` values the A1 emits around a finish or failure (`FINISH`, `FAILED` assumed; community docs list only `IDLE` in the example), and whether the A1 reports a state change when it is switched off mid-print.
- What the A1 reports in `print_error` and `hms` (structure and codes are undocumented in OpenBambuAPI); PrintOS only shows them raw.
- Task `status` semantics for cancelled prints, and whether `ams` data in tasks exposes per-filament grams for A1 + AMS lite.
- Cloud behaviour under load: real rate limits, MQTT session limits per account, reconnect behaviour after Bambu maintenance.
- The China region hosts.
- Developer-mode behaviour and the LAN protocol (not implemented).

## 5. Recommendation

1. **Cloud is the primary and only built-in connection.** It is the only method that works from Azure and on the phone away from home, it matches the read-only needs (status, history, completion events), and the implemented abstraction already isolates it.
2. **Stay read-only and low volume.** No commands, no print start, no camera via the cloud. This keeps PrintOS inside what Bambu says it tolerates.
3. **Treat the cloud as a risky dependency.** Everything that matters (print jobs, stock) must keep working when the connection is gone: connection loss produces "offline / stale" states, and finished prints can still be logged by hand or caught up by the history sync after reconnecting.
4. **Optional later: a bridge agent** (small program on a Raspberry Pi or PC on the home network, outbound-only HTTPS or WebSocket to PrintOS, authenticated with a per-printer key). It would read LAN MQTT (needs Developer mode or LAN Only mode and gives up the cloud for that printer, or runs alongside Bambu Connect) and could expose the camera stream. Only worth building if camera view, lower latency or cloud independence become real requirements. The printer monitoring screen reserves a "camera" slot with a clear "not available via cloud" state so the bridge can plug in later.
5. **Real-hardware validation checklist** before relying on the automation: connect with a real token, print a short job, cancel one, power the printer off mid-print, and compare the recorded state history, the created print job and the stock movement with reality. Section 4 is the list of what that run must settle.

## Sources

- OpenBambuAPI (community docs): https://github.com/Doridian/OpenBambuAPI (`mqtt.md`, `cloud-http.md`, `video.md`)
- Bambu Lab blog, "Updates and Third-Party Integration with Bambu Connect", 20 January 2025: https://blog.bambulab.com/updates-and-third-party-integration-with-bambu-connect
- Bambu Wiki, enable developer mode: https://wiki.bambulab.com/en/knowledge-sharing/enable-developer-mode (could not be fetched; claims taken from search summaries)
- Bambu Wiki, third party integration: https://wiki.bambulab.com/en/software/third-party-integration (could not be fetched)
- All3DP, LAN Only mode explained: https://all3dp.com/2/bambu-lab-lan-only-mode-explained/ (search result only)
- Cloud policy report, 12 November 2026 (secondary): https://www.tutkit.com/en/news/1081-bambu-lab-cloud-blocking-third-party-software

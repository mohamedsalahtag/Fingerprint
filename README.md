# ZK Device Manager

A web application to manage **ZKTeco fingerprint / face attendance terminals**: register devices,
connect over the network, read device information, **batch-download users + fingerprint + face
templates to SQL Server**, browse the stored data, and **copy users/templates from one device to
another**.

It replaces the device-management side of the legacy `ElAbd OMSManager` WinForms app with a modern,
single-deployment web app, using the same proven ZKTeco `zkemkeeper` COM SDK underneath.

## Stack
- **ASP.NET Core Blazor Server** (.NET 10, built **x64**) — UI + real-time job progress.
- **EF Core** → SQL Server `ksajedsvsql003`, database `ZKDeviceManager` (created automatically).
- **zkemkeeper COM SDK** (x64) for device communication, pinned to a single **STA** worker thread.
- Long operations run as **background jobs** with live progress; one device operation at a time.

## Solution layout
```
ZKDeviceManager.slnx
 ├─ src/ZKDeviceManager.Web/       Blazor Server UI, DI, background job host
 ├─ src/ZKDeviceManager.Data/      EF Core entities, AppDbContext, Migrations
 ├─ src/ZKDeviceManager.Devices/   zkemkeeper wrapper (IZkDeviceService), STA executor, job runner
 ├─ libs/Interop.zkemkeeper.dll    Prebuilt COM interop (x64)
 ├─ sdk/                           The 9 ZKTeco SDK DLLs (register zkemkeeper.dll)
 └─ tools/register-sdk.ps1         One-time COM registration helper
```

## Prerequisites (on the host that runs the app)
1. **Windows x64** with the .NET 10 runtime (or SDK).
2. **Network reach** to the terminals on **UDP/TCP port 4370** (across all device subnets).
3. **Registered SDK** — the `zkemkeeper` COM component must be registered (once):
   ```powershell
   # From an ELEVATED PowerShell, in the solution root:
   pwsh -File tools\register-sdk.ps1
   # To remove:  pwsh -File tools\register-sdk.ps1 -Unregister
   ```
   This registers `sdk\zkemkeeper.dll` (x64) in place via `System32\regsvr32.exe`; the eight sibling
   native DLLs in `sdk\` are loaded by it and must stay alongside it.
4. **SQL Server access** — the app connects to `ksajedsvsql003` with **Integrated Security**
   (the running user / app-pool identity needs rights to create & use the `ZKDeviceManager` DB).
   The connection string lives in `src/ZKDeviceManager.Web/appsettings.json`.

## Run
```powershell
dotnet run --project src\ZKDeviceManager.Web
```
On first start the app **creates the database and schema** automatically (EF migrations), then serves
the UI (default `http://localhost:5299` under the dev profile). Registering the SDK is only needed
before you actually connect to a device — the app boots and manages data without it.

## Using the app
- **Devices** — add a device (name, IP, port 4370, optional comm key, location). *Test* checks
  connectivity; *Download* queues a batch download of users + templates; *Details* shows live info.
- **Device Details** — read live serial/firmware/capacity/counts, sync the clock, enable/disable UI,
  restart or power off, and download.
- **Stored Data** — browse users + template counts saved in the database, per source device.
- **Copy / Transfer** — pick a **source** and **target** device and transfer users + templates,
  either **live** (read the source now) or from **previously saved** data.
- **Jobs** — live progress and history of every download/upload/copy, kept as an audit trail.

## How device communication works
All SDK calls go through `IZkDeviceService` (`ZkDeviceService`), which mirrors the proven call
sequences from the legacy app and the ZKTeco `UserInfo` sample:
- Connect: `Connect_Net(ip, 4370)`; bracket bulk ops with `EnableDevice(false/true)`.
- Users: `ReadAllUserID` → `SSR_GetAllUserInfo`; write `SSR_SetUserInfo`.
- Fingerprints: `ReadAllTemplate` → `GetUserTmpExStr` (fallback `SSR_GetUserTmpStr` for firmware
  < 6.60); write `SetUserTmpExStr` inside `BeginBatchUpdate`/`BatchUpdate`.
- Faces (index 50): `GetUserFaceStr` / `SetUserFaceStr` — pushed one-by-one (not batchable).
- Templates are stored as the SDK's string form and are portable between devices of the same
  algorithm/firmware family.

Because `zkemkeeper` uses the COM apartment (STA) model, **every** device call is funneled through a
single dedicated STA thread (`StaExecutor`), which also serializes access so two operations never hit
one terminal at once.

## Face templates — via PUSH/ADMS (not the COM SDK)
The legacy `zkemkeeper` face read (`GetUserFaceStr`) **spins at 100% CPU forever on modern Windows**
(confirmed on Windows 11 and Windows Server 2022; works only on old Windows 7). It is an SDK defect,
not the app. So faces use ZKTeco's **PUSH / ADMS** protocol instead — the terminal uploads its data
to the app over HTTP (endpoints under `/iclock/`), no COM involved.

- On the terminal: **Menu → Comm → Cloud Server / ADMS** → Server Address = this app's host IP,
  Port = the app's port (`8770` on the server). Enable Domain Name = OFF, HTTPS = OFF.
- In the app: open **Face / PUSH**. The terminal appears under *Connected terminals*.
  Click **Request users + faces** → it uploads users and all **face templates**
  (`DATA QUERY USERINFO` + `DATA QUERY BIODATA`). Faces then show under **Stored Data**.
- **Copy faces to another terminal:** on the same page pick a source device and click
  **Push users + faces →** at a connected terminal (device-to-device face copy on modern Windows).
- **Bonus:** ADMS mode also pushes **attendance in real time** automatically.

## Deployed instance
Runs as a **Windows Service** on Windows Server 2022 (`192.168.3.15`), port **8770** →
**http://192.168.3.15:8770**. Self-contained (no .NET install needed). Runs as LocalSystem; the
server machine account has `db_owner` on the DB (no stored passwords, no Kerberos double-hop).

## Verified
- `dotnet build` — solution builds clean (x64), interop resolved.
- App boots and **applies EF migrations to `ksajedsvsql003`** (DB + tables created).
- All pages (`/`, `/devices`, `/data`, `/copy`, `/jobs`) return HTTP 200.
- **Pending live hardware test**: register the SDK and point a real terminal — *Test Connection* and
  *Details* should return real values; *Download* should populate `EnrolledUsers` /
  `BiometricTemplates`; *Copy* should replicate users to a second device.

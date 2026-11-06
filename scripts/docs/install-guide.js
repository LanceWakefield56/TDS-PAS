// Generates docs/BrightPAS-Installation-Guide-<version>.docx
//   NODE_PATH=$(npm root -g) node scripts/docs/install-guide.js <out.docx> <version>
const fs = require('fs');
const { Document, Packer, Paragraph, TextRun, HeadingLevel, Table, TableRow, TableCell, WidthType, ShadingType,
  AlignmentType, LevelFormat, Footer, Header, PageNumber, TableOfContents, BorderStyle } = require('docx');

const OUT = process.argv[2] || 'BrightPAS-Installation-Guide.docx';
const VER = process.argv[3] || '0.3.0';
const SETUP = `BrightPAS-Setup-${VER}.exe`, WEB = `BrightPAS-Web-${VER}.zip`;
const NAVY = '1F5C7A', TEAL = '2E7DA0', LIGHT = 'D9EDF7', W = 9638;

const run = (t, o = {}) => new TextRun({ text: t, ...o });
// **bold**, `code`
const rich = (s) => s.split(/(\*\*[^*]+\*\*|`[^`]+`)/).filter(Boolean).map(x =>
  x.startsWith('**') ? run(x.slice(2, -2), { bold: true })
    : x.startsWith('`') ? run(x.slice(1, -1), { font: 'Consolas', size: 19, color: '7A2E00' }) : run(x));
const p = (s, o = {}) => new Paragraph({ children: rich(s), spacing: { after: 120 }, ...o });
const h1 = (s) => new Paragraph({ heading: HeadingLevel.HEADING_1, children: [run(s)], pageBreakBefore: true });
const h2 = (s) => new Paragraph({ heading: HeadingLevel.HEADING_2, children: [run(s)], keepNext: true });
const h3 = (s) => new Paragraph({ heading: HeadingLevel.HEADING_3, children: [run(s)], keepNext: true });
const bl = (items) => items.map(s => new Paragraph({ numbering: { reference: 'b', level: 0 }, children: rich(s), spacing: { after: 60 } }));
let NI = 0;
const nl = (items) => { const k = ++NI; return items.map(s => new Paragraph({ numbering: { reference: 'n', level: 0, instance: k }, children: rich(s), spacing: { after: 80 } })); };
const box = (label, fill, edge) => (s) => new Paragraph({ children: [run(label + '  ', { bold: true, color: edge }), ...rich(s)],
  shading: { type: ShadingType.CLEAR, fill, color: 'auto' }, border: { left: { style: BorderStyle.SINGLE, size: 24, color: edge, space: 6 } },
  spacing: { before: 120, after: 160 }, indent: { left: 120 } });
const note = box('NOTE', LIGHT, TEAL);
const ok = box('✔ WHAT YOU SHOULD SEE', 'E3F4E1', '2E7D32');
const warn = box('⚠ CAREFUL', 'FFF4D6', 'B7791F');
const stop = box('■ STOP', 'FBE3E3', 'B42318');
const tip = box('TIP', 'F1F1F7', '5B5B8F');
// Code block: one shaded paragraph per line. Type exactly what is shown.
const code = (text) => text.split('\n').map((line, i, a) => new Paragraph({
  children: [run(line || ' ', { font: 'Consolas', size: 19 })], shading: { type: ShadingType.CLEAR, fill: 'EEF1F4', color: 'auto' },
  spacing: { before: i === 0 ? 80 : 0, after: i === a.length - 1 ? 140 : 0 }, indent: { left: 240, right: 240 },
  border: i === 0 ? { top: { style: BorderStyle.SINGLE, size: 4, color: 'C5CDD5', space: 2 } } : (i === a.length - 1 ? { bottom: { style: BorderStyle.SINGLE, size: 4, color: 'C5CDD5', space: 2 } } : undefined) }));

const bd = { style: BorderStyle.SINGLE, size: 4, color: 'A6B8C4' };
const borders = { top: bd, bottom: bd, left: bd, right: bd };
function table(rows, widths) {
  const tot = widths.reduce((a, b) => a + b, 0);
  const ws = widths.map(w => Math.round(w * W / tot));
  ws[ws.length - 1] += W - ws.reduce((a, b) => a + b, 0);
  return new Table({
    width: { size: W, type: WidthType.DXA }, columnWidths: ws,
    rows: rows.map((r, i) => new TableRow({
      tableHeader: i === 0, cantSplit: true,
      children: r.map((c, j) => new TableCell({
        width: { size: ws[j], type: WidthType.DXA }, borders, margins: { top: 60, bottom: 60, left: 100, right: 100 },
        shading: i === 0 ? { type: ShadingType.CLEAR, fill: NAVY, color: 'auto' } : (i % 2 === 0 ? { type: ShadingType.CLEAR, fill: 'F2F8FC', color: 'auto' } : undefined),
        children: String(c).split('|').map(line => new Paragraph({ spacing: { after: 40 },
          children: i === 0 ? [run(line, { bold: true, color: 'FFFFFF', size: 18 })] : rich(line) }))
      }))
    }))
  });
}
const sp = () => new Paragraph({ children: [], spacing: { after: 80 } });
const C = [];
const add = (...xs) => xs.flat().forEach(x => C.push(x));

// ============================== TITLE ==============================
add(new Paragraph({ children: [], spacing: { before: 2000 } }),
  new Paragraph({ children: [run('TASMAN DAY SURGERY', { bold: true, color: TEAL, size: 28 })] }),
  new Paragraph({ children: [run('BrightPAS', { bold: true, color: NAVY, size: 64 })], spacing: { before: 200 } }),
  new Paragraph({ children: [run('Sandbox Installation and Test Guide', { color: NAVY, size: 36 })] }),
  new Paragraph({ children: [run('One server + one Windows 11 workstation — written for junior IT staff', { color: TEAL, size: 24 })], spacing: { after: 500 },
    border: { bottom: { style: BorderStyle.SINGLE, size: 12, color: TEAL, space: 8 } } }),
  table([
    ['Item', 'Detail'],
    ['BrightPAS version', VER],
    ['Files this guide uses', `${SETUP}|${WEB}`],
    ['Environment', 'Sandbox / test only — synthetic (made-up) patients only'],
    ['Time needed', 'About 3 hours the first time (most of it is waiting for installs)'],
    ['Date', 'November 2026'],
  ], [3, 7]),
  sp(),
  stop('Never enter real patient information into the sandbox. Use made-up names and the test NHI numbers in this guide only.'));

add(h1('How to use this guide'),
  p('Follow the parts **in order**, from Part A to Part I. Do not skip steps, even if they seem obvious. Each step tells you exactly what to click or type and what you should see afterwards. If what you see is different, stop and use **Part K – Troubleshooting**, then **Part J – Sending logs for help**.'),
  h3('The conventions in this guide'),
  table([
    ['When you see…', 'It means…'],
    ['**Bold words**', 'A button, menu, or option on screen. Click it or choose it.'],
    ['`Grey code text`', 'Something to type exactly as shown (letters, spaces and punctuation).'],
    ['A grey code box', 'Commands to type into PowerShell. Type (or copy and paste) the whole line, then press **Enter**.'],
    ['✔ WHAT YOU SHOULD SEE', 'The expected result. Check it before going on.'],
    ['⚠ CAREFUL', 'A step where people commonly make mistakes.'],
    ['■ STOP', 'Do not continue until this is right.'],
    ['BRIGHTPAS-SRV', 'The name we give the server in this guide.'],
    ['WIN11-TEST', 'The Windows 11 workstation you test from.'],
  ], [3, 7]),
  h3('How to open PowerShell as administrator'),
  p('Many steps use PowerShell. Every time this guide says **"open PowerShell as administrator"**, do this:'),
  nl(['Click the **Start** button (Windows logo, bottom-left).', 'Type `powershell`.', 'In the results, right-click **Windows PowerShell** and choose **Run as administrator**.',
    'If a box asks **"Do you want to allow this app to make changes to your device?"**, click **Yes**.',
    'A blue or black window opens. The title bar starts with **Administrator:**. If it does not say Administrator, close it and try again.']),
  tip('To paste into PowerShell, right-click inside the window (or press Ctrl+V). Then press Enter.'),
  h3('Take snapshots (checkpoints) as you go'),
  p('Both machines are virtual machines (VMs). Your virtualisation software (Hyper-V, VMware, Proxmox, etc.) can take a **checkpoint** (also called a **snapshot**) — a saved copy of the VM you can go back to in one click. This guide tells you when to take one. If something goes badly wrong later, you can go back to the last good checkpoint instead of starting again.'),
  h3('The record sheet'),
  p('Appendix C is a record sheet. Print it and fill it in as you go (IP address, website address, user names). **Never write passwords on it.**'));

// ============================== OVERVIEW ==============================
add(h1('What you are building'),
  p('You will set up two machines:'),
  table([
    ['Machine', 'What runs on it', 'What you do on it'],
    ['**BRIGHTPAS-SRV**|Windows Server 2022 or 2025', 'SQL Server 2022 (the database)|IIS (the web server)|BrightPAS (the web application)', 'Install everything. Run BrightPAS Setup. Read logs.'],
    ['**WIN11-TEST**|Windows 11', 'Just a web browser (Microsoft Edge)', 'Use BrightPAS the way staff will: sign in, register test patients, manage users.'],
  ], [2.2, 4, 3.8]),
  sp(),
  p('Staff will only ever use a web browser. Nothing is installed on their computers. All connections are encrypted (HTTPS), and everyone signs in with a password **plus** a 6-digit code from a phone app (multi-factor authentication, MFA).'),
  h2('The order of work'),
  table([
    ['Part', 'What', 'Where', 'Approx. time'],
    ['A', 'Prepare the server (name, time zone, updates)', 'Server', '30–60 min'],
    ['B', 'Install SQL Server 2022 Developer', 'Server', '30 min'],
    ['C', 'Copy the BrightPAS files and the .NET Hosting Bundle', 'Server', '10 min'],
    ['D', 'Run BrightPAS Setup — "Check this server"', 'Server', '5 min'],
    ['E', 'Run BrightPAS Setup — "Install"', 'Server', '20–30 min'],
    ['F', 'Prepare the Windows 11 workstation', 'Workstation + server', '20 min'],
    ['G', 'Set up the first administrator (with MFA)', 'Workstation', '10 min'],
    ['H', 'Test checklist', 'Workstation', '45 min'],
    ['I', 'Security, penetration and audit self-test', 'Server', '5 min'],
    ['J', 'Where the logs are and how to send them', 'Server', 'As needed'],
    ['K', 'Troubleshooting', '—', 'As needed'],
    ['L–M', 'Upgrading to a new version; resetting the sandbox', 'Server', 'As needed'],
  ], [0.8, 5.2, 2.2, 1.8]));

// ============================== BEFORE YOU START ==============================
add(h1('Before you start'),
  h2('Virtual machine sizes'),
  table([
    ['Setting', 'BRIGHTPAS-SRV', 'WIN11-TEST'],
    ['Operating system', 'Windows Server 2022 or 2025 Standard, **Desktop Experience** (the version with the normal Windows desktop, not "Core")', 'Windows 11 Pro (or Enterprise)'],
    ['Processors (vCPU)', '4', '2'],
    ['Memory (RAM)', '16 GB recommended (8 GB minimum — Setup will warn)', '4 GB or more'],
    ['Disk', '100 GB (minimum 60 GB)', '64 GB'],
    ['Network', 'Same virtual network/switch as the workstation', 'Same as the server'],
    ['Internet', 'Needed for downloads and Windows Update', 'Optional'],
  ], [2.2, 4.6, 3.2]),
  h2('Accounts'),
  bl(['A **local administrator** account on the server (for example the built-in **Administrator**). Use the same account for every server step in this guide.',
    'A normal or administrator account on the workstation.',
    'A **smartphone** with an authenticator app. **Microsoft Authenticator** is recommended (free in the App Store / Google Play). Google Authenticator also works.']),
  h2('Files you need'),
  table([
    ['File', 'Where to get it', 'What it is'],
    [`**${SETUP}**`, 'OneDrive › TDS-PAS › Installers', 'The BrightPAS installer and test tool (one file).'],
    [`**${WEB}**`, 'OneDrive › TDS-PAS › Installers', 'The BrightPAS web application. **Do not unzip it** — Setup uses the zip as it is.'],
    ['**dotnet-hosting-10.0.x-win.exe**', 'Download in Part C (Microsoft website)', 'The Microsoft .NET 10 runtime and its IIS module.'],
    ['**SQL2022-SSEI-Dev.exe**', 'Download in Part B (Microsoft website)', 'SQL Server 2022 Developer edition installer.'],
    ['This guide', 'OneDrive › TDS-PAS › Installers', 'Keep it open on another screen or print it.'],
  ], [3.4, 3.2, 3.4]),
  note('SQL Server **Developer** edition is free and has every feature, but it is licensed **for testing only**. The live system will use Tasman Day Surgery\'s licensed SQL Server 2022 Standard. BrightPAS works the same on both.'));

// ============================== PART A ==============================
add(h1('Part A — Prepare the server'),
  p('Do all of Part A on **BRIGHTPAS-SRV**, signed in as the local administrator.'),
  h2('A1. Set the time zone to New Zealand'),
  p('MFA codes depend on the clock being right, and BrightPAS records times in the audit log, so the time zone must be correct.'),
  nl(['Open PowerShell as administrator (see "How to use this guide").', 'Type this and press **Enter**:']),
  ...code('Set-TimeZone -Id "New Zealand Standard Time"\nGet-TimeZone'),
  ok('The second command shows **Id : New Zealand Standard Time** and **DisplayName : (UTC+12:00) Auckland, Wellington**. "Standard Time" is the Windows name; daylight saving is handled automatically.'),
  p('Also check the clock in the bottom-right corner shows the correct NZ time. If it is wrong by more than a minute, run:'),
  ...code('w32tm /resync'),
  h2('A2. Rename the server to BRIGHTPAS-SRV'),
  nl(['In the same PowerShell window, type:']),
  ...code('Rename-Computer -NewName "BRIGHTPAS-SRV" -Restart'),
  ok('The server restarts within a few seconds. Sign in again as the same administrator.'),
  note('If the server already has another name you want to keep, that is fine — use that name wherever this guide says BRIGHTPAS-SRV.'),
  h2('A3. Install Windows updates'),
  nl(['Click **Start › Settings › Windows Update** (on Server 2022: **Start › Settings › Update & Security › Windows Update**).', 'Click **Check for updates**. Let all updates download and install.',
    'Click **Restart now** when asked.', 'Sign in again and repeat until it says **You\'re up to date**.']),
  warn('This can take 30 minutes or more on a new server. Do not skip it — some installs fail on un-updated servers.'),
  h2('A4. Write down the server\'s IP address'),
  p('Open PowerShell as administrator and type:'),
  ...code('Get-NetIPAddress -AddressFamily IPv4 | Format-Table InterfaceAlias, IPAddress'),
  ok('A small table. Ignore **127.0.0.1** (that is the server talking to itself). The other address — for example **192.168.1.50** or **10.0.0.20** — is the server\'s IP address. Write it on the record sheet (Appendix C).'),
  tip('If this address changes after a restart, the workstation will lose the server (Part F). Ask whoever manages the network/virtualisation to give the server a fixed address (a "static IP" or "DHCP reservation").'),
  h2('A5. Take a checkpoint'),
  p('In your virtualisation software, take a checkpoint of BRIGHTPAS-SRV called **"A – prepared"**.'));

// ============================== PART B ==============================
add(h1('Part B — Install SQL Server 2022 Developer'),
  p('All steps on **BRIGHTPAS-SRV**.'),
  h2('B1. Download the installer'),
  nl(['Open **Microsoft Edge** on the server.', 'Go to: `https://www.microsoft.com/sql-server/sql-server-downloads`',
    'Scroll down to **Developer** (it says "free, full-featured edition licensed for use as a development and test database"). Click **Download now** under Developer.',
    'If the page offers several versions, choose **SQL Server 2022**.',
    'Edge saves **SQL2022-SSEI-Dev.exe** to your **Downloads** folder.']),
  h2('B2. Run the installer — choose "Basic"'),
  nl(['Open **File Explorer › Downloads**. Double-click **SQL2022-SSEI-Dev.exe**. Click **Yes** to allow changes.',
    'On "Select an installation type", click **Basic**.',
    'Click **Accept** on the licence terms.',
    'Leave the install location as it is. Click **Install**.',
    'Wait. It downloads about 1–2 GB and installs. This takes 10–30 minutes.']),
  ok('A screen that says **"Installation has completed successfully!"** with **INSTANCE NAME: MSSQLSERVER** and a connection string starting **Server=localhost**. Take a photo or screenshot of this screen.'),
  p('Click **Close**. You do **not** need to click "Install SSMS" (SQL Server Management Studio). It is optional and not needed for this guide.'),
  note('Why Basic? It installs only the database engine, as the default instance, using **Windows authentication** (no SQL passwords), and makes the account you are signed in with an administrator of SQL Server. That is exactly what BrightPAS Setup expects.'),
  warn('If you ever install SQL Server with **Custom** instead: choose **Database Engine Services** only, **Default instance**, **Windows authentication mode**, and click **Add Current User** on the "Database Engine Configuration" page.'),
  h2('B3. Turn on TCP/IP for SQL Server'),
  p('SQL Server installs with its network protocol (TCP/IP) switched off. BrightPAS connects using TCP/IP, even on the same server, so it must be turned on.'),
  nl(['Press **Windows key + R**. In the Run box type `SQLServerManager16.msc` and press **Enter**. Click **Yes** if asked. (This opens **SQL Server 2022 Configuration Manager**.)',
    'In the left pane, expand **SQL Server Network Configuration** and click **Protocols for MSSQLSERVER**.',
    'In the right pane, right-click **TCP/IP** and click **Enable**.',
    'A message says the change takes effect after the service restarts. Click **OK**.',
    'In the left pane, click **SQL Server Services**.',
    'In the right pane, right-click **SQL Server (MSSQLSERVER)** and click **Restart**. Wait until its State is **Running**.',
    'Close Configuration Manager.']),
  h2('B4. Test that SQL Server is listening'),
  p('Open PowerShell as administrator and type:'),
  ...code('Test-NetConnection localhost -Port 1433'),
  ok('The last line says **TcpTestSucceeded : True**.'),
  stop('If it says **False**: go back to B3 and check TCP/IP shows **Enabled** and the service was restarted. BrightPAS cannot install until this says True.'),
  warn('Do **not** open port 1433 in the Windows Firewall. On a single server, SQL Server should only be reachable from the server itself. The security self-test (Part I) checks this.'),
  h2('B5. Take a checkpoint'),
  p('Take a checkpoint of BRIGHTPAS-SRV called **"B – SQL installed"**. This is the best point to come back to if you want to re-test the BrightPAS install from scratch.'));

// ============================== PART C ==============================
add(h1('Part C — Copy the BrightPAS files to the server'),
  h2('C1. Create the setup folder'),
  p('Open PowerShell as administrator and type:'),
  ...code('New-Item -ItemType Directory -Path C:\\BrightPASSetup -Force'),
  h2('C2. Copy the two BrightPAS files into it'),
  p(`Copy **${SETUP}** and **${WEB}** into **C:\\BrightPASSetup**. Any of these ways is fine:`),
  bl(['Sign in to OneDrive in Edge on the server (`https://onedrive.live.com`), open **TDS-PAS › Installers**, download both files, then move them from Downloads to C:\\BrightPASSetup.',
    'Copy and paste through your VM console (for example Hyper-V "Enhanced Session").',
    'A shared folder or USB drive mapped to the VM.']),
  warn(`Do **not** unzip ${WEB}. Do not rename either file. Setup looks for the zip by its name.`),
  h2('C3. Download the .NET 10 Hosting Bundle into the same folder'),
  nl(['In Edge on the server go to: `https://dotnet.microsoft.com/download/dotnet/10.0`',
    'Find the section **ASP.NET Core Runtime 10.0.x** (x is the newest number).',
    'In the **Windows** row, click **Hosting Bundle**.',
    'When it has downloaded, move the file (named like **dotnet-hosting-10.0.x-win.exe**) into **C:\\BrightPASSetup**.']),
  warn('Do **not** run the Hosting Bundle yourself. It must be installed **after** IIS, and BrightPAS Setup installs IIS first and then the Hosting Bundle automatically, in the right order.'),
  h2('C4. Unblock the downloaded files'),
  p('Windows marks downloaded files as "from the internet", which can stop them running. In PowerShell (as administrator) type:'),
  ...code('Get-ChildItem C:\\BrightPASSetup | Unblock-File\nGet-ChildItem C:\\BrightPASSetup | Format-Table Name, Length'),
  ok(`Three files are listed: **${SETUP}** (about 16 MB), **${WEB}**, and **dotnet-hosting-10.0.x-win.exe** (about 100 MB).`));

// ============================== PART D ==============================
add(h1('Part D — BrightPAS Setup: check the server'),
  p('This first run **changes nothing**. It checks the server is ready and tells you in plain English what to fix.'),
  h2('D1. Start Setup as administrator'),
  nl(['Open **File Explorer** and go to **C:\\BrightPASSetup**.', `Right-click **${SETUP}** and choose **Run as administrator**. Click **Yes**.`,
    'If a blue box says **"Windows protected your PC"**: click **More info**, then **Run anyway**. (The sandbox build is not yet digitally signed; the live version will be.)']),
  ok('A black window titled **BrightPAS Setup ' + VER + '** with a menu of options 1 to 5.'),
  h2('D2. Choose option 1'),
  nl(['Type `1` and press **Enter**.', 'It asks **"SQL Server name to test"** with **[localhost]** in brackets. Just press **Enter** (that accepts the value in brackets).']),
  h2('D3. Read the results'),
  p('Each check shows **PASS** (green), **WARN** (yellow) or **FAIL** (red). On a brand-new server, this is normal:'),
  table([
    ['Check', 'Expected result now', 'Why'],
    ['Windows edition / 64-bit / Administrator', 'PASS', '—'],
    ['Memory (RAM)', 'PASS (WARN if 8–15 GB)', 'WARN is fine for a sandbox.'],
    ['Free disk space', 'PASS', '—'],
    ['Time zone', 'PASS', 'If WARN, redo step A1.'],
    ['IIS (Internet Information Services)', '**FAIL – expected**', 'Setup installs IIS for you in Part E.'],
    ['.NET 10 ASP.NET Core runtime', '**FAIL – expected**', 'Setup installs the Hosting Bundle in Part E.'],
    ['ASP.NET Core Module for IIS', '**FAIL – expected**', 'Comes with the Hosting Bundle.'],
    ['Old TLS versions disabled', 'WARN – expected', 'Setup secures this in Part E.'],
    ['SQL Server connection', '**PASS – must pass**', 'If not, see Part K ("SQL Server connection fails").'],
  ], [3.6, 2.6, 3.8]),
  stop('Any **FAIL** other than the three "expected" ones above must be fixed first. Each FAIL line includes **"What to do"** — follow it, then run option 1 again.'),
  p('At the end Setup shows **"Full log file:"** and where it was saved. Press **Enter** to close.'),
  tip('Optional: run Setup again and choose **3 (Preview)**. It lists every step the install would do, without changing anything.'));

// ============================== PART E ==============================
add(h1('Part E — BrightPAS Setup: install'),
  h2('E1. Start Setup and choose option 2'),
  nl([`Right-click **${SETUP}** › **Run as administrator** › **Yes** (and **More info › Run anyway** if needed).`, 'Type `2` and press **Enter**.']),
  h2('E2. Answer the questions exactly like this'),
  table([
    ['Setup asks…', 'You type…', 'Meaning'],
    ['SQL Server name (single server: localhost) [localhost]', 'Press **Enter**', 'SQL Server is on this same server.'],
    ['Database name [BrightPAS]', 'Press **Enter**', 'The database will be called BrightPAS.'],
    ['Connect to SQL Server with your Windows account (recommended)? [Y/n]', 'Press **Enter**', 'Uses your Windows sign-in. No SQL password needed.'],
    ['Is SQL Server using its own self-signed certificate…? [Y/n]', 'Press **Enter**', 'Normal for a new sandbox SQL Server.'],
    ['Ready to install. This changes this server\'s IIS and security settings. Continue? [y/N]', 'Type `y` then **Enter**', '**The default here is No** — you must type y.'],
  ], [4.6, 2, 3.4]),
  sp(),
  note('A capital letter in brackets is the default. **[Y/n]** means pressing Enter is Yes. **[y/N]** means pressing Enter is No.'),
  h2('E3. Wait while Setup works'),
  p('Setup runs 12 steps. Each one prints its name, what it did, and how long it took. Every step is **safe to repeat** — if you run Setup again, finished steps are skipped.'),
  table([
    ['#', 'Step', 'Typical time', 'What happens'],
    ['1', 'Check this server', 'seconds', 'Same checks as Part D.'],
    ['2', 'Install IIS (web server)', '3–10 min', 'Installs only the IIS parts BrightPAS needs. **The window may look stuck — do not close it.**'],
    ['3', 'Secure network encryption (TLS)', 'seconds', 'Allows only TLS 1.2 and 1.3 (modern encryption). Needs a restart at the end.'],
    ['4', 'Harden IIS', 'seconds', 'Removes the default website and unsafe features, hides server details.'],
    ['5', 'Create the PAS database', 'seconds', 'Creates the BrightPAS database.'],
    ['6', 'Create or upgrade the database tables', '< 1 min', 'Creates tables, including the tamper-evident audit ledger.'],
    ['7', 'Prepare first administrator set-up', 'seconds', 'Creates the one-time set-up code.'],
    ['8', 'Install the .NET 10 Hosting Bundle', '1–3 min', 'Runs the Microsoft installer you downloaded, silently.'],
    ['9', 'Create the application\'s database login', 'seconds', 'Lets the website reach the database using its own Windows identity (**IIS APPPOOL\\BrightPAS**) with read/write rights only — no password exists to steal.'],
    ['10', 'Install the PAS web application files', '< 1 min', 'Unzips the web app to **C:\\Program Files\\BrightPAS\\Web** and locks down folder permissions.'],
    ['11', 'Publish the PAS website in IIS (HTTPS only)', '< 1 min', 'Creates the BrightPAS website with a sandbox (self-signed) certificate. No plain HTTP.'],
    ['12', 'Check the website is running', 'seconds', 'Opens the site to make sure it answers.'],
  ], [0.5, 3.2, 1.5, 4.8]),
  sp(),
  stop('If any step turns **red**, Setup stops and prints **"INSTALLATION STOPPED"**. Read the red message — it says what went wrong and what to do. Setup then offers to **collect a diagnostics zip** — type `y`. Then see Part K and Part J. After fixing the problem, run option 2 again.'),
  h2('E4. Write down the one-time set-up code'),
  ok('Green text **"INSTALLATION COMPLETE."** then, in yellow, a code like **ABCD-EFGH-JKLM-NPQR**.'),
  warn('**Write the code on paper now.** It is shown only once, is **not** saved in any log, and works for **7 days**. You need it in Part G. If you lose it, just run Setup option 2 again — it makes a new code (until the first administrator has been set up).'),
  h2('E5. Write down the website address'),
  p('Setup prints **"Open BrightPAS from the Windows 11 workstation:"** followed by an address such as **https://BRIGHTPAS-SRV/** (it may include a longer name like https://BRIGHTPAS-SRV.yourdomain.local/). Write the exact address on the record sheet.'),
  h2('E6. The security self-test question'),
  p('Setup asks **"Run the security, penetration and audit self-test now?"**. You can type `n` for now — you will run it properly in Part I after the first administrator exists. (Typing `y` is also fine; it will show a warning that no administrator exists yet.)'),
  h2('E7. Restart the server'),
  p('Press **Enter** to close Setup. Then, in PowerShell as administrator:'),
  ...code('Restart-Computer'),
  p('Sign in again after the restart.'),
  h2('E8. Quick check on the server itself'),
  nl(['Open Edge on the server and go to `https://localhost/`.',
    'Edge shows **"Your connection isn\'t private"**. This is expected on the server because the sandbox certificate is not trusted there. Click **Advanced** › **Continue to localhost (unsafe)**.']),
  ok('The BrightPAS page **"Welcome to BrightPAS — First-time set-up"** asking for the one-time code. **Do not enter it here** — close Edge. You will do this from the workstation in Part G.'),
  h2('E9. Take a checkpoint'),
  p('Take a checkpoint of BRIGHTPAS-SRV called **"E – BrightPAS ' + VER + ' installed"**.'));

// ============================== PART F ==============================
add(h1('Part F — Prepare the Windows 11 workstation'),
  p('The workstation needs three things: to reach the server over the network, to know the server\'s name, and to trust the server\'s sandbox certificate.'),
  h2('F1. Check the workstation can reach the server'),
  p('On **WIN11-TEST**, open PowerShell (as administrator) and type (use the server IP address from your record sheet):'),
  ...code('Test-NetConnection 192.168.1.50 -Port 443'),
  ok('**TcpTestSucceeded : True**.'),
  p('If it says **False**, on the **server**:'),
  nl(['Press **Windows key + R**, type `wf.msc`, press **Enter** (Windows Defender Firewall with Advanced Security).',
    'Click **Inbound Rules**. Find **World Wide Web Services (HTTPS Traffic-In)**.',
    'If its **Enabled** column says **No**, right-click it › **Enable Rule**.',
    'Try the test on the workstation again. If still False, check both VMs are on the same virtual switch/network.']),
  note('A failed "ping" does not matter — Windows Server blocks ping by default. Use Test-NetConnection as above.'),
  h2('F2. Tell the workstation the server\'s name (hosts file)'),
  p('In a sandbox there is often no DNS server, so we add the server\'s name to the workstation\'s **hosts** file. In PowerShell **as administrator** on the workstation, type (change the IP address, and use the name from the website address Setup printed):'),
  ...code('Add-Content -Path "$env:windir\\System32\\drivers\\etc\\hosts" -Value "`r`n192.168.1.50`tBRIGHTPAS-SRV"\nTest-NetConnection BRIGHTPAS-SRV -Port 443'),
  ok('The second command shows **TcpTestSucceeded : True**.'),
  warn('If the address Setup printed was a longer name such as **BRIGHTPAS-SRV.yourdomain.local**, add that longer name in the hosts line instead of (or as well as) BRIGHTPAS-SRV.'),
  h2('F3. Trust the sandbox certificate'),
  p('BrightPAS uses HTTPS. In the sandbox, Setup made its own (self-signed) certificate. Browsers do not trust it until you tell the workstation to. The live system will use a proper certificate and will not need this step.'),
  h3('On the server: export the certificate'),
  p('Open PowerShell as administrator on **BRIGHTPAS-SRV** and type (all on one line):'),
  ...code("Get-ChildItem Cert:\\LocalMachine\\My | Where-Object FriendlyName -eq 'BrightPAS (self-signed)' | Select-Object -First 1 | Export-Certificate -FilePath C:\\BrightPASSetup\\BrightPAS-sandbox.cer"),
  ok('A line showing **BrightPAS-sandbox.cer** in C:\\BrightPASSetup. This file contains only the public part of the certificate — it is safe to copy. The private key cannot be exported (by design).'),
  p('Copy **BrightPAS-sandbox.cer** to the workstation\'s **Downloads** folder (OneDrive, shared folder, or VM copy and paste).'),
  h3('On the workstation: import it as trusted'),
  p('In PowerShell **as administrator** on WIN11-TEST:'),
  ...code('Import-Certificate -FilePath "$env:USERPROFILE\\Downloads\\BrightPAS-sandbox.cer" -CertStoreLocation Cert:\\LocalMachine\\Root'),
  ok('A line with a **Thumbprint** and **Subject CN=BRIGHTPAS-SRV…**.'),
  tip('Prefer clicking? Double-click the .cer file › **Install Certificate…** › **Local Machine** › **Next** › **Place all certificates in the following store** › **Browse** › **Trusted Root Certification Authorities** › **OK** › **Next** › **Finish** › "The import was successful".'),
  warn('Trusting a self-signed certificate is for the **sandbox only**. Never do this on staff computers in the live system.'),
  h2('F4. Open BrightPAS'),
  nl(['Close **all** Edge windows (so Edge picks up the new certificate), then open Edge again.', 'Go to the website address from your record sheet, for example `https://BRIGHTPAS-SRV/`.']),
  ok('A padlock in the address bar (no warning) and the page **"Welcome to BrightPAS — First-time set-up"**.'),
  p('Take a checkpoint of WIN11-TEST called **"F – ready"**.'));

// ============================== PART G ==============================
add(h1('Part G — Set up the first administrator'),
  p('This creates the first BrightPAS account, with the **System Administrator** role. Do it on the workstation.'),
  h2('G1. Get your phone ready'),
  p('Install **Microsoft Authenticator** on your phone (or Google Authenticator). Open it once so it is ready.'),
  h2('G2. Enter the one-time code'),
  nl(['On the BrightPAS first-time set-up page, type the code from Part E4 (the dashes are optional).', 'Click the button to continue.']),
  note('If it says the code is wrong or expired, run Setup option 2 on the server again to get a new code.'),
  h2('G3. Create your account'),
  nl(['**Your full name** — for example `Test Administrator`.', '**Username** — for example `admin.test`. Write it on the record sheet.',
    '**Password** — at least **12 characters**. A short sentence works well, for example three or four random words. Common passwords and passwords that contain your username are rejected.',
    '**Type the password again**, then continue.']),
  h2('G4. Connect your phone (MFA)'),
  nl(['BrightPAS shows a **QR code**.', 'In Microsoft Authenticator tap **+** (Add account) › **Other account (Google, Facebook, etc.)** › scan the QR code.',
    'The app now shows **BrightPAS** with a 6-digit code that changes every 30 seconds.', 'Type the current 6-digit code into BrightPAS and continue.']),
  h2('G5. Save the recovery codes'),
  p('BrightPAS shows a list of **recovery codes**. Each can be used once to sign in if the phone is lost.'),
  warn('Write them down or print them and keep them somewhere safe. They are shown only once. Tick **"I have saved my recovery codes"** and continue.'),
  h2('G6. Sign out and sign in again'),
  nl(['Click your name / **Sign out**.', 'Sign in with your username and password, then the current 6-digit code from the phone app.']),
  ok('You see **My work today**, and in the menu **Find a patient**, **Register a patient**, **User accounts** and **Audit log**.'),
  note('The one-time set-up code no longer works now that an administrator exists. That is correct — it cannot be used to make a second "first" administrator.'));

// ============================== PART H ==============================
const T = (rows) => table([['#', 'Test', 'Steps', 'Expected result', '✓'], ...rows.map((r, i) => [String(i + 1), ...r, '☐'])], [0.4, 1.8, 3.8, 3.4, 0.4]);
add(h1('Part H — Test checklist'),
  p('Work through this on the workstation. Tick each test that behaves as expected. If something does not, write down what happened, take a screenshot (**Windows key + Shift + S**), and send it with the logs (Part J).'),
  stop('Synthetic data only. Use made-up names. The NHI numbers below are official test-format numbers.'),
  h2('H1. User accounts and roles'),
  T([
    ['Create a nurse', 'As admin: **User accounts › Add a user**. Name `Nora Nurse`, username `nora.nurse`, a temporary password (12+ characters), tick role **Nurse** › **Create account**.', 'Account appears in the list.'],
    ['Create a privacy officer', 'Same, name `Pat Privacy`, username `pat.privacy`, role **Privacy Officer**.', 'Account appears in the list.'],
    ['New user first sign-in', 'Open an **InPrivate** Edge window (Ctrl+Shift+N). Sign in as nora.nurse. Add her to the phone app by scanning the QR code.', 'Nora must set up MFA before she can do anything. Recovery codes are shown.'],
    ['Role limits', 'While signed in as Nora, look at the menu.', 'Nora sees **Find a patient** and **Register a patient**, but **not** User accounts or Audit log.'],
    ['Direct URL blocked', 'As Nora, type `https://BRIGHTPAS-SRV/admin/users` in the address bar.', 'Access is refused / no user list is shown.'],
  ]),
  h2('H2. Patients'),
  T([
    ['Register a patient', 'As Nora: **Register a patient**. Name `Aroha Test-Patient`, a made-up date of birth, NHI `ZSC21TN`, choose an ethnicity › **Save**.', 'Patient is saved and gets an MRN (medical record number).'],
    ['Invalid NHI rejected', 'Register another patient with NHI `ZSC21TA`.', 'BrightPAS says the NHI is not valid (check digit wrong).'],
    ['Duplicate warning', 'Register a patient with the **same name and date of birth** as Aroha, NHI `ZVB97XQ`.', 'BrightPAS warns of a possible duplicate and asks you to confirm.'],
    ['Macron-insensitive search', 'Register `Māia Test-Macron`. Then **Find a patient** and search `Maia`.', 'Māia is found even though you typed no macron.'],
    ['Search by NHI', 'Search `ZSC21TN`.', 'Aroha is found.'],
  ]),
  h2('H3. Sign-in security'),
  T([
    ['Wrong MFA code', 'Sign in as Nora with the right password but code `000000`.', 'Refused.'],
    ['Lockout', 'Enter a wrong password for Nora **5 times**.', 'Account is locked for 15 minutes.'],
    ['Admin unlock', 'As admin: **User accounts** › Nora › **Unlock**.', 'Nora can sign in again straight away.'],
    ['Reset phone app', 'As admin: Nora › **Reset phone app**. Sign in as Nora.', 'Nora must scan a new QR code (old phone entry no longer works).'],
    ['Session timeout', 'Sign in and leave BrightPAS untouched for **16 minutes**, then click something.', 'You are asked to sign in again.'],
    ['Deactivate', 'As admin: Nora › **Deactivate**. Try to sign in as Nora. Then **Reactivate**.', 'Deactivated user cannot sign in; reactivated user can.'],
    ['Last admin protected', 'As admin, try to deactivate **your own** account.', 'Refused — BrightPAS will not let you lock out the last administrator.'],
  ]),
  h2('H4. Audit'),
  T([
    ['Audit log entries', 'Sign in as **pat.privacy** (set up MFA first). Open **Audit log**.', 'Entries for every action above: sign-ins, failed sign-ins, lockout, patient registrations, user changes — with user, time and computer.'],
    ['Filter', 'Filter by user `nora.nurse` › **Filter**.', 'Only Nora\'s actions are shown.'],
    ['Integrity check', 'Click **Check audit log integrity**.', '**"Audit log intact: all N records verified"**.'],
  ]));

// ============================== PART I ==============================
add(h1('Part I — Security, penetration and audit self-test'),
  p('BrightPAS Setup includes a built-in test that checks the installation the way an attacker or auditor would. It is **safe and read-only**: it sends the same kinds of requests an attacker might, but changes nothing. Run it after every install or upgrade.'),
  h2('I1. Run it'),
  nl([`On **BRIGHTPAS-SRV**, right-click **${SETUP}** › **Run as administrator**.`, 'Type `4` and press **Enter**.',
    'Press **Enter** for each question (localhost, BrightPAS, Windows account = Yes, self-signed = Yes). For the website address, press **Enter** to accept **https://localhost/**.']),
  p('It takes about one minute.'),
  h2('I2. What it checks'),
  table([
    ['Area', 'What is tested'],
    ['Website (penetration probes)', 'Website reachable; HTTPS certificate; unencrypted HTTP disabled; old encryption off and TLS 1.2 on; SQL injection on the sign-in form; reflected script injection (XSS); cross-site request forgery (CSRF) protection; sensitive files and path traversal; error details hidden; TRACE method disabled; server header removed; session cookie flags; password-guessing and set-up-code-guessing protection (rate limits).'],
    ['Web server (IIS)', 'IIS installed; BrightPAS site HTTPS-only; Default Web Site removed; directory browsing off; risky IIS features removed; site runs with its own limited identity.'],
    ['Files and permissions', 'Program folder, settings file, and key/log folder permissions.'],
    ['Database (SQL Server)', 'Connection is encrypted; website database account has least privilege; sa disabled; xp_cmdshell disabled; encryption at rest (TDE); backups; recovery model; SQL Server version.'],
    ['Server (Windows)', 'Windows Firewall on; SMBv1 disabled; Guest account disabled; Remote Desktop requires network-level authentication; antivirus real-time protection; Windows updates.'],
    ['Application accounts and data', 'System Administrators; MFA set up for all users; accounts awaiting phone-app set-up; first-administrator code status; break-glass reviews; check for real-looking (non-test) NHI numbers.'],
    ['Audit log', 'Tamper-proof (ledger) storage; integrity of every record (hash chain); failed sign-ins in the last 24 hours.'],
  ], [2.2, 7.8]),
  h2('I3. Read the result'),
  p('Each line is **PASS**, **WARN**, **FAIL**, or **INFO**, with **"What to do"** for anything not passing. The last line gives the overall result.'),
  table([
    ['Expected WARN in the sandbox', 'Why it is OK in the sandbox', 'What happens in the live system'],
    ['Self-signed website certificate', 'Sandbox certificate made by Setup.', 'A proper certificate is installed.'],
    ['TDE (database encryption at rest) is off', 'Not needed for synthetic data.', 'Turned on as part of go-live.'],
    ['No recent database backup', 'Sandbox is not backed up.', 'Backups and the isolated standby copy are configured.'],
    ['"sa" login enabled (may not appear)', 'Only if SQL was installed with mixed-mode authentication.', 'sa is disabled.'],
    ['Windows updates / antivirus (may appear)', 'Sandbox VMs are often behind on updates.', 'Patched monthly; antivirus on.'],
  ], [3.4, 3.3, 3.3]),
  sp(),
  stop('Any **FAIL** is a real problem. Do not ignore it. Send the report (below) and the diagnostics zip (Part J).'),
  note('The test deliberately makes a few failed sign-ins with usernames starting **sectest-**. They appear in the audit log — that proves auditing works. The rate-limit test may make sign-in say "too many attempts" for about a minute afterwards; just wait.'),
  h2('I4. The report'),
  p('Setup saves the report as a web page and a text file in **C:\\ProgramData\\BrightPAS\\Reports** (named **BrightPAS-Security-Report-<date>.html**) and puts a copy next to Setup.exe. Double-click the .html file to open it in Edge.'));

// ============================== PART J ==============================
add(h1('Part J — Where the logs are and how to send them'),
  h2('J1. The easy way: Setup option 5'),
  nl([`Right-click **${SETUP}** › **Run as administrator**.`, 'Type `5` and press **Enter**.',
    'Setup collects all the logs, the relevant Windows event log entries, the IIS and certificate settings, and the BrightPAS configuration into **one zip file**.']),
  ok('**"Diagnostics saved: C:\\ProgramData\\BrightPAS\\Support\\BrightPAS-Diagnostics-BRIGHTPAS-SRV-<date>.zip"** and a copy next to Setup.exe in C:\\BrightPASSetup.'),
  note('The zip contains logs and settings only — no patient data. Passwords are masked (replaced with ********). The one-time set-up code is never logged.'),
  h2('J2. Sending it'),
  nl(['In OneDrive, open the **TDS-PAS** folder and create a folder called **Logs** (once only).',
    'Copy the diagnostics zip (and any security report and screenshots) into **TDS-PAS › Logs**.',
    'Tell Claude: "logs are in the Logs folder", plus one sentence about what you were doing when it went wrong. Claude can read that folder directly.']),
  h2('J3. Where each log lives (if you want to look yourself)'),
  table([
    ['What', 'Where'],
    ['Setup logs (every run of Setup)', 'C:\\ProgramData\\BrightPAS\\Logs\\BrightPAS-<Install|Check|SecurityTest|Diagnostics>-<date>.log|(a copy is also next to Setup.exe)'],
    ['Security self-test reports', 'C:\\ProgramData\\BrightPAS\\Reports'],
    ['Diagnostics zips', 'C:\\ProgramData\\BrightPAS\\Support'],
    ['.NET Hosting Bundle install log', '%TEMP%\\BrightPAS-HostingBundle-<date>.log  (type %TEMP% in the File Explorer address bar)'],
    ['Website errors', 'Event Viewer › Windows Logs › **Application** — sources **IIS AspNetCore Module V2** and **.NET Runtime**'],
    ['IIS request logs', 'C:\\inetpub\\logs\\LogFiles\\W3SVC<number>'],
  ], [3.4, 6.6]),
  tip('**C:\\ProgramData** is a hidden folder. To open it, click in the File Explorer address bar, type `C:\\ProgramData\\BrightPAS` and press Enter.'),
  h2('J4. More detail on screen'),
  p('To see every detail on screen while Setup runs (it is always in the log file anyway), start it from PowerShell as administrator with **--verbose**:'),
  ...code(`cd C:\\BrightPASSetup\n.\\${SETUP} install --verbose`));

// ============================== PART K ==============================
add(h1('Part K — Troubleshooting'),
  p('Find the symptom in the left column. Try the fix. If it still does not work, collect diagnostics (Part J) and send them.'),
  table([
    ['Symptom', 'Likely cause', 'What to do'],
    ['"Windows protected your PC" when starting Setup', 'Sandbox build is not code-signed yet.', 'Click **More info › Run anyway**. Also run step C4 (Unblock-File).'],
    ['Setup says "not running as administrator"', 'Double-clicked instead of Run as administrator.', 'Close it. Right-click › **Run as administrator**.'],
    ['SQL Server connection fails / TcpTestSucceeded False', 'TCP/IP not enabled or SQL service stopped.', 'Redo B3 and B4. In Configuration Manager check **SQL Server (MSSQLSERVER)** is **Running**.'],
    ['SQL error **18456** (login failed)', 'You are signed in to Windows as a different account from the one that installed SQL Server.', 'Sign in as the account used in Part B, or add your account as a SQL administrator (re-run SQL setup › Add features, or ask for help).'],
    ['Certificate / "trust" error talking to SQL', 'Answered No to the self-signed SQL question.', 'Run Setup again and answer **Y** (Enter) to the self-signed question.'],
    ['Step 2 (IIS) seems stuck', 'Windows is installing features.', 'Wait up to 15 minutes. Do not close the window.'],
    ['Step 8 says Hosting Bundle not installed / not found', 'The dotnet-hosting file is not in C:\\BrightPASSetup, or it is the wrong file.', 'Redo C3. The file name must start **dotnet-hosting-10.** and end **-win.exe**. Run Setup option 2 again.'],
    ['Step 10 says web package not found', `${WEB} is not next to Setup.exe, was renamed, or was unzipped.`, 'Copy the original zip into C:\\BrightPASSetup (redo C2).'],
    ['Website shows **HTTP Error 500.19**', 'Hosting Bundle was installed before IIS, or is damaged.', 'Run the dotnet-hosting file in C:\\BrightPASSetup and choose **Repair**. Then `iisreset` in PowerShell (admin).'],
    ['Website shows **HTTP Error 500.30** or **500.31**', 'The BrightPAS app failed to start (often database access).', 'Event Viewer › Windows Logs › Application: look for red errors from **IIS AspNetCore Module V2** / **.NET Runtime** at that time. Run Setup option 2 again (repairs the database login). Then send diagnostics.'],
    ['Website shows **503 Service Unavailable**', 'The BrightPAS app pool has stopped (it stops after repeated crashes).', 'PowerShell (admin): `Start-WebAppPool BrightPAS`. If it stops again, collect diagnostics.'],
    ['Workstation: "can\'t reach this page"', 'Name not in hosts file, wrong IP, or firewall.', 'Redo F1 and F2. Check the server IP has not changed (A4).'],
    ['Workstation: "Your connection isn\'t private"', 'Certificate not trusted, or opened by a name not on the certificate.', 'Redo F3, close all Edge windows. Use exactly the address Setup printed.'],
    ['MFA code always wrong', 'Phone or server clock is wrong.', 'Set phone time to automatic. On the server run `w32tm /resync` and check the time zone (A1).'],
    ['Lost the one-time set-up code', '—', 'Run Setup option 2 again; it makes a new code (only until the first admin exists).'],
    ['Locked out of the only admin account', 'Too many wrong passwords.', 'Wait 15 minutes. If the phone is lost, use a recovery code.'],
    ['"Too many attempts" on sign-in', 'Rate limiting (also triggered by the security self-test).', 'Wait one minute and try again.'],
  ], [3.1, 3.1, 3.8]));

// ============================== PART L / M ==============================
add(h1('Part L — Upgrading to a new version'),
  nl(['Take a checkpoint of the server first.',
    'Copy the new **BrightPAS-Setup-<new>.exe** and **BrightPAS-Web-<new>.zip** into **C:\\BrightPASSetup**. Keep the dotnet-hosting file. Delete the old BrightPAS-Setup and BrightPAS-Web files so you do not run the wrong one.',
    'Run C4 (Unblock-File) again.',
    'Run the **new** Setup as administrator, choose **2**, and give the same answers as Part E.',
    'Setup upgrades the database tables and the web app. Your users and test patients are kept. No new set-up code is shown (an administrator already exists).',
    'Run Setup option **4** (security self-test) and do a quick sign-in from the workstation.']),
  h1('Part M — Resetting the sandbox'),
  h2('Easiest: go back to a checkpoint'),
  p('Revert BRIGHTPAS-SRV to **"B – SQL installed"** and repeat from Part C. Revert the workstation to **"F – ready"** (you will need to redo F3 if the certificate changed).'),
  h2('Manual reset (removes BrightPAS and all test data)'),
  warn('This permanently deletes the BrightPAS database. Sandbox only.'),
  p('PowerShell as administrator on the server:'),
  ...code('Import-Module WebAdministration\nRemove-Website -Name BrightPAS -ErrorAction SilentlyContinue\nRemove-WebAppPool -Name BrightPAS -ErrorAction SilentlyContinue\nsqlcmd -S localhost -E -C -Q "ALTER DATABASE BrightPAS SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE BrightPAS;"\nsqlcmd -S localhost -E -C -Q "IF SUSER_ID(\'IIS APPPOOL\\BrightPAS\') IS NOT NULL DROP LOGIN [IIS APPPOOL\\BrightPAS];"\nRemove-Item "C:\\Program Files\\BrightPAS" -Recurse -Force -ErrorAction SilentlyContinue\nRemove-Item "C:\\ProgramData\\BrightPAS" -Recurse -Force -ErrorAction SilentlyContinue'),
  p('Then run Setup option 2 again (Part E).'));

// ============================== APPENDICES ==============================
add(h1('Appendix A — Glossary'),
  table([
    ['Term', 'Plain-English meaning'],
    ['BrightPAS', 'Tasman Day Surgery\'s patient administration system (PAS).'],
    ['IIS', 'Internet Information Services — Microsoft\'s web server, built into Windows Server. It "hosts" the BrightPAS website.'],
    ['SQL Server', 'Microsoft\'s database. Stores all BrightPAS data.'],
    ['.NET Hosting Bundle', 'Microsoft software that lets IIS run applications written in .NET (BrightPAS is).'],
    ['App pool', 'The separate, locked-down Windows process IIS runs BrightPAS in. Its identity is IIS APPPOOL\\BrightPAS.'],
    ['HTTPS / TLS', 'Encryption between browser and server. The padlock in the address bar.'],
    ['Certificate', 'The server\'s digital ID card for HTTPS. Self-signed = made by the server itself (sandbox only).'],
    ['MFA', 'Multi-factor authentication: password plus a 6-digit code from a phone app.'],
    ['Recovery code', 'A one-use backup code to sign in if the phone is lost.'],
    ['One-time set-up code', 'The code Setup shows once so the first administrator can be created securely.'],
    ['Ledger table', 'A SQL Server table that can only be added to, never changed or deleted. BrightPAS\'s audit log uses one.'],
    ['Hash chain', 'Each audit record contains a fingerprint of the one before it, so any tampering is detected by the integrity check.'],
    ['NHI', 'National Health Index number — every NZ patient\'s unique identifier. The test ones in this guide are not real people.'],
    ['MRN', 'Medical record number — Tasman Day Surgery\'s own patient number, given by BrightPAS.'],
    ['Checkpoint / snapshot', 'A saved copy of a virtual machine you can return to.'],
    ['Hosts file', 'A small file that tells a computer "this name = this IP address".'],
  ], [2.6, 7.4]),
  h1('Appendix B — Setup command reference'),
  p('Most people use the menu. For scripted or repeat installs, Setup also accepts commands (run from PowerShell as administrator in C:\\BrightPASSetup):'),
  ...code(`.\\${SETUP}                       # menu\n.\\${SETUP} check   --sql-server localhost\n.\\${SETUP} install --sql-server localhost --trust-server-certificate\n.\\${SETUP} install --sql-server localhost --what-if        # preview only\n.\\${SETUP} security-test --url https://localhost/\n.\\${SETUP} diagnostics\n\nCommon options:  --verbose  (every detail on screen)   --quiet  (no questions)\nWeb options:     --host-name NAME  --https-port 443  --certificate-thumbprint THUMBPRINT\n                 --install-dir PATH  --web-package PATH  --skip-iis  --skip-web\nExit codes:      0 = OK   1 = OK with warnings   2 = failed / not ready   3 = unexpected error`),
  note('Never put a password on the command line. If a SQL login is used without questions (--quiet --sql-user NAME), Setup reads the password from the environment variable BRIGHTPAS_SQL_PASSWORD.'),
  h1('Appendix C — Record sheet'),
  p('Print this page and fill it in. **Do not write passwords, recovery codes or the set-up code here.**'),
  table([
    ['Item', 'Value'],
    ['Server name', 'BRIGHTPAS-SRV'],
    ['Server IP address (A4)', ' '],
    ['Server administrator account used', ' '],
    ['SQL Server instance (B2)', 'MSSQLSERVER (default) — localhost'],
    ['BrightPAS version installed', VER],
    ['Website address printed by Setup (E5)', ' '],
    ['Certificate thumbprint (F3)', ' '],
    ['First administrator username (G3)', ' '],
    ['Test users created (H1)', 'nora.nurse, pat.privacy'],
    ['Checkpoints taken', '☐ A – prepared   ☐ B – SQL installed   ☐ E – BrightPAS installed   ☐ F – ready'],
    ['Security self-test result (I3)', 'Pass ___  Warn ___  Fail ___'],
    ['Date / tested by', ' '],
  ], [4, 6]));

const doc = new Document({
  creator: 'Tasman Day Surgery', title: 'BrightPAS Sandbox Installation and Test Guide',
  styles: {
    default: { document: { run: { font: 'Calibri', size: 21 } } },
    paragraphStyles: [
      { id: 'Heading1', name: 'Heading 1', basedOn: 'Normal', next: 'Normal', quickFormat: true, run: { size: 32, bold: true, color: NAVY, font: 'Calibri' }, paragraph: { spacing: { before: 240, after: 160 }, outlineLevel: 0 } },
      { id: 'Heading2', name: 'Heading 2', basedOn: 'Normal', next: 'Normal', quickFormat: true, run: { size: 25, bold: true, color: TEAL, font: 'Calibri' }, paragraph: { spacing: { before: 220, after: 100 }, outlineLevel: 1 } },
      { id: 'Heading3', name: 'Heading 3', basedOn: 'Normal', next: 'Normal', quickFormat: true, run: { size: 22, bold: true, color: NAVY, font: 'Calibri' }, paragraph: { spacing: { before: 160, after: 80 }, outlineLevel: 2 } },
    ],
  },
  numbering: { config: [
    { reference: 'b', levels: [{ level: 0, format: LevelFormat.BULLET, text: '•', alignment: AlignmentType.LEFT, style: { paragraph: { indent: { left: 500, hanging: 260 } } } }] },
    { reference: 'n', levels: [{ level: 0, format: LevelFormat.DECIMAL, text: '%1.', alignment: AlignmentType.LEFT, style: { paragraph: { indent: { left: 500, hanging: 300 } } } }] },
  ] },
  features: { updateFields: true },
  sections: [{
    properties: { page: { size: { width: 11906, height: 16838 }, margin: { top: 1134, bottom: 1134, left: 1134, right: 1134 } } },
    headers: { default: new Header({ children: [new Paragraph({ alignment: AlignmentType.RIGHT, children: [run(`BrightPAS ${VER}  |  Sandbox Installation and Test Guide`, { size: 16, color: '7A8A96' })] })] }) },
    footers: { default: new Footer({ children: [new Paragraph({ alignment: AlignmentType.CENTER, children: [run('Tasman Day Surgery – Internal  |  Page ', { size: 16, color: '7A8A96' }), new TextRun({ children: [PageNumber.CURRENT], size: 16, color: '7A8A96' })] })] }) },
    children: [...C.slice(0, 8), new Paragraph({ children: [], pageBreakBefore: true }), new Paragraph({ heading: HeadingLevel.HEADING_1, children: [run('Contents')] }),
      new TableOfContents('Contents', { hyperlink: true, headingStyleRange: '1-2' }), ...C.slice(8)],
  }],
});
Packer.toBuffer(doc).then(b => { fs.writeFileSync(OUT, b); console.log('written ' + OUT); });

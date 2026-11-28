import { useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import { api, errText, fileToBase64, nzDateTime, Perm } from '../api';
import { useSession } from '../App';
import FileImport from '../components/FileImport';
import { DocNode, Errors, errs, FORMAT_LABEL, FORMATS, MsgNav, StatusBadge, TRANSPORT_LABEL, TreeView } from '../components/MsgShared';
import Modal from '../components/Modal';
const HANDLER_LABEL: Record<string, string> = { LabResults: 'Lab results', DeviceObservations: 'Bedside monitors', AnaesthesiaRecord: 'SaferSleep record' };

function Head({ title, sub }: { title: string; sub?: string }) {
  return (<><div className="page-head"><h1>{title}</h1>{sub && <p className="muted">{sub}</p>}</div><MsgNav /></>);
}

// ---------------------------------------------------------------- dashboard
export function MessagingDashboard() {
  const { can } = useSession();
  const [d, setD] = useState<any>(null);
  const [error, setError] = useState<string | null>(null);
  const load = () => api('/api/messaging/dashboard').then(setD).catch((e) => setError(errText(e)));
  useEffect(() => { load(); const t = setInterval(load, 15000); return () => clearInterval(t); }, []);
  const clear = async (id: string) => { try { await api(`/api/messaging/alerts/${id}/clear`, { body: {} }); load(); } catch (e) { setError(errText(e)); } };
  return (
    <div className="stack-lg">
      <Head title="Messaging & integration" sub="Every interface in and out of BrightPAS. Refreshes every 15 seconds." />
      {error && <p className="error">{error}</p>}
      {d && d.alerts.length > 0 && <section className="card stack warn-box"><h2>Alerts</h2>
        {d.alerts.map((a: any) => <div key={a.id} className="row-between"><span><strong>{a.interface}</strong> – {a.detail} <span className="muted small">({nzDateTime(a.raisedAt)})</span></span>
          {can(Perm.Integration) && <button className="btn btn-light btn-small" onClick={() => clear(a.id)}>Clear</button>}</div>)}
      </section>}
      {!d ? <p>Loading…</p> : (
        <div className="iface-grid">
          {d.interfaces.map(({ i, state }: any) => {
            const light = !i.isActive ? 'off' : i.openAlerts > 0 || (state && !state.running) ? 'bad' : i.failed24h > 0 ? 'warn' : 'good';
            return (
              <Link key={i.id} to={`/messaging/messages?interfaceId=${i.id}`} className={`card iface-card ${light}`}>
                <div className="row-between"><strong>{i.name}</strong><span className={`light ${light}`} aria-label={light} /></div>
                <div className="small muted">{i.direction === 'Inbound' ? '⇢ in' : 'out ⇢'} · {FORMAT_LABEL[i.format]} · {TRANSPORT_LABEL[i.transport]?.replace(/^(Receive|Send) – /, '')}</div>
                <div className="small">{!i.isActive ? 'Switched off' : state ? (state.running ? `Running${state.connections ? ` · ${state.connections} connection(s)` : ''}` : `Stopped: ${state.problem}`) : i.transport === 'Monitored' ? 'Recorded automatically' : ''}</div>
                <div className="iface-stats small"><span>24 h: {i.total24h}</span><span>Waiting: {i.queued}</span><span className={i.failed24h ? 'warn-text' : ''}>Failed: {i.failed24h}</span><span className={i.deadLetters ? 'warn-text' : ''}>Gave up: {i.deadLetters}</span></div>
                <div className="small muted">Last in: {nzDateTime(i.lastIn)} · Last out: {nzDateTime(i.lastOut)}</div>
              </Link>
            );
          })}
          {d.interfaces.length === 0 && <p className="muted">No interfaces yet. {can(Perm.Integration) && <Link to="/messaging/interfaces">Add the first one</Link>}</p>}
        </div>
      )}
      {d && d.problems.length > 0 && <section className="card stack"><h2>Messages needing attention</h2><MessageTable rows={d.problems} /></section>}
    </div>
  );
}

function MessageTable({ rows }: { rows: any[] }) {
  return (
    <table className="results compact"><thead><tr><th>When</th><th>Interface</th><th>Type</th><th>Reference</th><th>Status</th><th>Problem</th></tr></thead>
      <tbody>{rows.map((m) => (
        <tr key={m.id}><td data-label="When"><Link to={`/messaging/messages/${m.id}`}>{nzDateTime(m.createdAt)}</Link></td><td data-label="Interface">{m.direction === 'Inbound' ? '⇢ ' : ''}{m.interface}{m.direction === 'Outbound' ? ' ⇢' : ''}</td>
          <td data-label="Type">{m.messageType ?? '—'}</td><td data-label="Reference" className="small">{m.reference ?? m.controlId ?? ''}{m.nhi && <div className="muted">{m.nhi}</div>}</td>
          <td data-label="Status"><StatusBadge status={m.status} />{m.attempts > 1 && <div className="small muted">{m.attempts} tries</div>}</td><td data-label="Problem" className="small">{m.lastError ?? ''}</td></tr>))}</tbody></table>
  );
}

// ---------------------------------------------------------------- interfaces
const blankIface = { id: null, code: '', name: '', direction: 'Inbound', transport: 'MllpListen', format: 'Hl7v2', handler: 'None', host: '', port: 2575, folderPath: '', url: '', secret: null as string | null,
  aeTitle: 'BRIGHTPAS', allowedClients: [] as any[], isActive: true, alertAfterFailures: 3, silentAlertHours: null as number | null, notes: '',
  sqlText: '', pollSeconds: 300 as number | null, watermarkColumn: '' };

export function MessagingInterfaces() {
  const { can } = useSession();
  const [list, setList] = useState<any[] | null>(null);
  const [edit, setEdit] = useState<any>(null);
  const [errors, setErrors] = useState<string[] | null>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const [inject, setInject] = useState<{ id: string; name: string; content: string; result?: any } | null>(null);
  const [importTo, setImportTo] = useState<{ id: string; name: string } | null>(null);
  const load = () => api<any[]>('/api/messaging/interfaces').then(setList).catch((e) => setErrors(errs(e)));
  useEffect(() => { load(); }, []);
  const save = async () => {
    setErrors(null); setMsg(null);
    try { await api('/api/messaging/interfaces', { body: { ...edit, port: edit.port ? Number(edit.port) : null, silentAlertHours: edit.silentAlertHours ? Number(edit.silentAlertHours) : null, pollSeconds: edit.pollSeconds ? Number(edit.pollSeconds) : null } }); setEdit(null); setMsg('Saved. Listeners start within 15 seconds.'); load(); }
    catch (x) { setErrors(errs(x)); }
  };
  const test = async (i: any) => { setMsg(null); setErrors(null); try { setMsg(`${i.name}: ${(await api(`/api/messaging/interfaces/${i.id}/test`, { body: {} })).result}`); } catch (x) { setErrors(errs(x)); } };
  const sendTest = async () => {
    if (!inject) return;
    try { setInject({ ...inject, result: await api(`/api/messaging/interfaces/${inject.id}/inject`, { body: { content: inject.content } }) }); load(); } catch (x) { setInject({ ...inject, result: { error: errText(x) } }); }
  };
  const genKey = async () => setEdit({ ...edit, secret: (await api('/api/messaging/interfaces/generate-key', { body: {} })).key });
  const inbound = (t: string) => ['MllpListen', 'DicomListen', 'HttpReceive', 'FolderWatch', 'SqlQuery'].includes(t);
  const setT = (t: string) => setEdit({ ...edit, transport: t, direction: inbound(t) ? 'Inbound' : 'Outbound', format: t === 'DicomListen' ? 'Dicom' : t.startsWith('Mllp') ? 'Hl7v2' : t.startsWith('Sql') ? 'Json' : edit.format, handler: inbound(t) ? edit.handler : 'None', port: t === 'DicomListen' ? 11112 : t.startsWith('Mllp') ? 2575 : edit.port });
  return (
    <div className="stack-lg">
      <Head title="Interfaces" sub="Communication points: where messages come from and where they go." />
      {msg && <p className="ok-text">{msg}</p>}
      <Errors list={errors} />
      {can(Perm.Integration) && !edit && <div><button className="btn btn-primary" onClick={() => setEdit({ ...blankIface })}>Add interface</button></div>}
      {edit && (
        <Modal wide title={edit.id ? `Change ${edit.name}` : 'New interface'} onClose={() => { setEdit(null); setErrors(null); }}>
          <div className="form-grid">
            <label>Name<input value={edit.name} onChange={(e) => setEdit({ ...edit, name: e.target.value })} placeholder="e.g. Awanui lab results" /></label>
            <label>Code (short, used in logs)<input value={edit.code} onChange={(e) => setEdit({ ...edit, code: e.target.value.toLowerCase() })} placeholder="e.g. awanui-in" /></label>
            <label>How messages arrive or leave<select value={edit.transport} onChange={(e) => setT(e.target.value)} disabled={!!edit.id && edit.transport === 'Monitored'}>
              {Object.entries(TRANSPORT_LABEL).filter(([k]) => k !== 'Monitored').map(([k, v]) => <option key={k} value={k}>{v}</option>)}</select></label>
            <label>Message format<select value={edit.format} onChange={(e) => setEdit({ ...edit, format: e.target.value })}>{FORMATS.map((f) => <option key={f} value={f}>{FORMAT_LABEL[f]}</option>)}</select></label>
            {edit.direction === 'Inbound' && <label>Hand to BrightPAS module<select value={edit.handler} onChange={(e) => setEdit({ ...edit, handler: e.target.value })}>
              <option value="None">None – only run flows</option><option value="LabResults">Lab results inbox (HL7 ORU)</option><option value="DeviceObservations">Bedside monitors (HL7 ORU vital signs)</option><option value="AnaesthesiaRecord">SaferSleep anaesthetic record (HL7 ORU)</option></select></label>}
            {['MllpListen', 'DicomListen', 'MllpSend'].includes(edit.transport) && <label>Port<input type="number" value={edit.port ?? ''} onChange={(e) => setEdit({ ...edit, port: e.target.value })} /></label>}
            {edit.transport === 'MllpSend' && <label>Receiving system (host name or IP)<input value={edit.host ?? ''} onChange={(e) => setEdit({ ...edit, host: e.target.value })} /></label>}
            {edit.transport === 'DicomListen' && <label>BrightPAS AE title<input value={edit.aeTitle ?? ''} onChange={(e) => setEdit({ ...edit, aeTitle: e.target.value.toUpperCase() })} maxLength={16} /></label>}
            {['FolderWatch', 'FolderWrite', 'DicomListen'].includes(edit.transport) && <label>{edit.transport === 'DicomListen' ? 'Archive folder for received files (optional)' : 'Folder on the server'}<input value={edit.folderPath ?? ''} onChange={(e) => setEdit({ ...edit, folderPath: e.target.value })} placeholder="D:\Interfaces\Labs\In" /></label>}
            {edit.transport === 'HttpPost' && <label>Web address<input value={edit.url ?? ''} onChange={(e) => setEdit({ ...edit, url: e.target.value })} placeholder="https://partner.example.nz/fhir/Bundle" /></label>}
            {edit.transport === 'HttpPost' && <label>Authorization header (optional, stored encrypted){edit.hasSecret && <span className="muted small"> – one is saved; leave blank to keep</span>}<input type="password" value={edit.secret ?? ''} onChange={(e) => setEdit({ ...edit, secret: e.target.value })} placeholder="Bearer …" autoComplete="off" /></label>}
            {edit.transport.startsWith('Sql') && <label>Connection string (stored encrypted){edit.hasSecret && <span className="muted small"> – one is saved; leave blank to keep</span>}
              <input type="password" value={edit.secret ?? ''} onChange={(e) => setEdit({ ...edit, secret: e.target.value })} placeholder="Server=db01;Database=Lab;User ID=brightpas_read;Password=…;Encrypt=True" autoComplete="off" /></label>}
            {edit.transport === 'SqlQuery' && <label>Run every (seconds)<input type="number" min={30} value={edit.pollSeconds ?? ''} onChange={(e) => setEdit({ ...edit, pollSeconds: e.target.value })} /></label>}
            {edit.transport === 'SqlQuery' && <label>Watermark column (optional – remembers where it got to)<input value={edit.watermarkColumn ?? ''} onChange={(e) => setEdit({ ...edit, watermarkColumn: e.target.value })} placeholder="UpdatedAt" /></label>}
            {edit.transport === 'HttpReceive' && <div className="stack-sm"><span>Key other systems must send (X-BrightPAS-Key){edit.hasSecret && <span className="muted small"> – one is saved</span>}</span>
              <div className="row"><input aria-label="Key" value={edit.secret ?? ''} onChange={(e) => setEdit({ ...edit, secret: e.target.value })} autoComplete="off" /><button type="button" className="btn btn-light btn-small" onClick={genKey}>Generate key</button></div>
              {edit.secret && <p className="hint">Copy this key now and give it to the other system securely – it is stored encrypted and not shown again.</p>}</div>}
            <label>Alert after this many failures in a row<input type="number" min={1} value={edit.alertAfterFailures} onChange={(e) => setEdit({ ...edit, alertAfterFailures: Number(e.target.value) })} /></label>
            <label>Alert if nothing for (hours, optional)<input type="number" min={1} value={edit.silentAlertHours ?? ''} onChange={(e) => setEdit({ ...edit, silentAlertHours: e.target.value })} /></label>
          </div>
          {['MllpListen', 'DicomListen'].includes(edit.transport) && (
            <div className="stack-sm"><strong>{edit.transport === 'DicomListen' ? 'Registered devices (IP address and AE title must both match)' : 'Systems allowed to connect (IP addresses)'}</strong>
              {edit.allowedClients.map((c: any, i: number) => (
                <div className="row" key={i}>
                  <input aria-label="IP address" value={c.address} placeholder="192.168.10.21" onChange={(e) => setEdit({ ...edit, allowedClients: edit.allowedClients.map((x: any, j: number) => j === i ? { ...x, address: e.target.value } : x) })} />
                  {edit.transport === 'DicomListen' && <input aria-label="AE title" value={c.aeTitle ?? ''} placeholder="ENDO1" onChange={(e) => setEdit({ ...edit, allowedClients: edit.allowedClients.map((x: any, j: number) => j === i ? { ...x, aeTitle: e.target.value.toUpperCase() } : x) })} />}
                  <input aria-label="Description" value={c.description ?? ''} placeholder="e.g. Endoscopy tower room 1" onChange={(e) => setEdit({ ...edit, allowedClients: edit.allowedClients.map((x: any, j: number) => j === i ? { ...x, description: e.target.value } : x) })} />
                  <button type="button" className="btn btn-link" onClick={() => setEdit({ ...edit, allowedClients: edit.allowedClients.filter((_: any, j: number) => j !== i) })}>Remove</button>
                </div>))}
              <div><button type="button" className="btn btn-light btn-small" onClick={() => setEdit({ ...edit, allowedClients: [...edit.allowedClients, { address: '', aeTitle: '', description: '' }] })}>+ Add</button></div>
              {edit.transport === 'MllpListen' && edit.allowedClients.length === 0 && <p className="warn-text small">Any computer can connect. List the lab's interface engine address.</p>}
            </div>)}
          {edit.transport.startsWith('Sql') && <label>{edit.transport === 'SqlQuery' ? 'SELECT statement (each row becomes a JSON message)' : 'Statement run for each message – values come from the JSON fields as @parameters'}
            <textarea rows={4} className="mono" value={edit.sqlText ?? ''} onChange={(e) => setEdit({ ...edit, sqlText: e.target.value })}
              placeholder={edit.transport === 'SqlQuery' ? 'SELECT Nhi, TestCode, Result, UpdatedAt FROM dbo.Results WHERE (@watermark IS NULL OR UpdatedAt > @watermark) ORDER BY UpdatedAt' : 'INSERT INTO dbo.TdsBookings (Nhi, Procedure, BookedFor) VALUES (@nhi, @procedure, @date)'} />
            <span className="hint">One statement, no semicolons or comments. Use a database login that can only do this (least privilege).</span></label>}
          <label>Notes<input value={edit.notes ?? ''} onChange={(e) => setEdit({ ...edit, notes: e.target.value })} /></label>
          <label className="check"><input type="checkbox" checked={edit.isActive} onChange={(e) => setEdit({ ...edit, isActive: e.target.checked })} /> Switched on</label>
          <div className="row"><button className="btn btn-primary" onClick={save}>Save</button><button className="btn btn-light" onClick={() => { setEdit(null); setErrors(null); }}>Cancel</button></div>
        </Modal>)}
      {inject && (
        <Modal wide title={`Send a test message into ${inject.name}`} onClose={() => setInject(null)}>
          <p className="hint">The message is processed exactly as if it had arrived (flows run and outputs are queued). Use synthetic test patients only.</p>
          <textarea rows={10} className="mono" value={inject.content} onChange={(e) => setInject({ ...inject, content: e.target.value })} />
          <div className="row"><button className="btn btn-primary" onClick={sendTest}>Process</button><button className="btn btn-light" onClick={() => setInject(null)}>Close</button></div>
          {inject.result && (inject.result.error ? <p className="error">{inject.result.error}</p> : <p>Result: <StatusBadge status={inject.result.status} /> {inject.result.error} <Link to={`/messaging/messages/${inject.result.messageId}`}>Open message</Link>{inject.result.ack && <code className="mono-box small">{inject.result.ack}</code>}</p>)}
        </Modal>)}
      {!list ? <p>Loading…</p> : (
        <table className="results"><thead><tr><th>Name</th><th>Connection</th><th>Format</th><th>Status</th><th /></tr></thead>
          <tbody>{list.map((i) => (
            <tr key={i.id}><td data-label="Name"><strong>{i.name}</strong><div className="small muted">{i.code}</div></td>
              <td data-label="Connection" className="small">{TRANSPORT_LABEL[i.transport]}<div className="muted">{i.port ? `port ${i.port}` : ''}{i.host ? ` ${i.host}` : ''}{i.aeTitle ? ` AE ${i.aeTitle}` : ''}{i.folderPath ?? ''}{i.url ?? ''}{i.transport === 'HttpReceive' ? `/api/integration/in/${i.code}` : ''}</div>
                {i.allowedClients.length > 0 && <div className="muted">{i.allowedClients.map((c: any) => c.aeTitle ? `${c.aeTitle}@${c.address}` : c.address).join(', ')}</div>}</td>
              <td data-label="Format">{FORMAT_LABEL[i.format]}{i.handler !== 'None' && <div className="small muted">→ {HANDLER_LABEL[i.handler] ?? i.handler}</div>}</td>
              <td data-label="Status">{i.isActive ? <span className="badge ok">On</span> : <span className="badge off">Off</span>}</td>
              <td className="actions">{can(Perm.Integration) && i.transport !== 'Monitored' && <>
                <button className="btn btn-link" onClick={() => setEdit({ ...i, secret: null })}>Change</button>
                <button className="btn btn-link" onClick={() => test(i)}>Test connection</button>
                {i.direction === 'Inbound' && <button className="btn btn-link" onClick={async () => setInject({ id: i.id, name: i.name, content: (await api(`/api/messaging/samples/${i.format}`)).sample })}>Send test message</button>}
                {i.direction === 'Inbound' && <button className="btn btn-link" onClick={() => setImportTo({ id: i.id, name: i.name })}>Import from file</button>}</>}</td></tr>))}</tbody></table>)}
      {importTo && <div className="stack-sm">
        <FileImport url={`/api/messaging/interfaces/${importTo.id}/import`} title={`Import into ${importTo.name}`} onDone={load}
          hint="When this connection is down, import the messages from a file instead – a USB stick, a network share or a file the sending system saved (HL7 files may hold many messages; ZIPs are opened). Each message is received, stored and processed exactly as if it had arrived live." />
        <div><button className="btn btn-light" onClick={() => setImportTo(null)}>Close import</button></div></div>}
    </div>
  );
}

// ---------------------------------------------------------------- messages
export function MessagingMessages() {
  const [params, setParams] = useSearchParams();
  const [rows, setRows] = useState<any[] | null>(null);
  const [ifaces, setIfaces] = useState<any[]>([]);
  const [error, setError] = useState<string | null>(null);
  const q = Object.fromEntries(params.entries());
  useEffect(() => { api<any[]>('/api/messaging/interfaces').then(setIfaces).catch(() => setIfaces([])); }, []);
  useEffect(() => { api<any[]>(`/api/messaging/messages?${params.toString()}`).then(setRows).catch((e) => setError(errText(e))); }, [params]);
  const set = (k: string, v: string) => setParams((p) => { if (v) p.set(k, v); else p.delete(k); return p; });
  return (
    <div className="stack-lg">
      <Head title="Messages" sub="Find any message by interface, patient NHI, reference or date. Opening a message is recorded in the audit log." />
      <div className="row filters">
        <label>Interface<select value={q.interfaceId ?? ''} onChange={(e) => set('interfaceId', e.target.value)}><option value="">All</option>{ifaces.map((i) => <option key={i.id} value={i.id}>{i.name}</option>)}</select></label>
        <label>Status<select value={q.status ?? ''} onChange={(e) => set('status', e.target.value)}><option value="">All</option><option value="problems">Problems</option><option value="waiting">Waiting to send</option>
          {['Processed', 'Sent', 'Recorded', 'Filtered', 'Failed', 'DeadLetter', 'Rejected'].map((s) => <option key={s} value={s}>{s === 'DeadLetter' ? 'Gave up' : s}</option>)}</select></label>
        <label>NHI<input value={q.nhi ?? ''} onChange={(e) => set('nhi', e.target.value.toUpperCase())} maxLength={7} /></label>
        <label>Reference / control ID<input value={q.text ?? ''} onChange={(e) => set('text', e.target.value)} /></label>
        <label>From<input type="date" value={q.from ?? ''} onChange={(e) => set('from', e.target.value)} /></label>
        <label>To<input type="date" value={q.to ?? ''} onChange={(e) => set('to', e.target.value)} /></label>
      </div>
      {error && <p className="error">{error}</p>}
      {!rows ? <p>Loading…</p> : rows.length === 0 ? <p className="muted">No messages match.</p> : <MessageTable rows={rows} />}
    </div>
  );
}

export function MessagingMessage() {
  const { id } = useParams();
  const { can } = useSession();
  const nav = useNavigate();
  const [d, setD] = useState<any>(null);
  const [view, setView] = useState<'tree' | 'raw'>('tree');
  const [reason, setReason] = useState('');
  const [errors, setErrors] = useState<string[] | null>(null);
  useEffect(() => { setD(null); api(`/api/messaging/messages/${id}`).then(setD).catch((e) => setErrors(errs(e))); }, [id]);
  const replay = async () => { setErrors(null); try { const r = await api(`/api/messaging/messages/${id}/replay`, { body: { reason } }); nav(`/messaging/messages/${r.id}`); } catch (x) { setErrors(errs(x)); } };
  if (!d) return <div className="stack-lg"><Head title="Message" /><Errors list={errors} />{!errors && <p>Loading…</p>}</div>;
  const m = d.message;
  return (
    <div className="stack-lg">
      <Head title={`${m.messageType ?? 'Message'} ${m.controlId ?? ''}`} sub={`${m.direction === 'Inbound' ? 'Received on' : 'Sent on'} ${m.interface} · ${nzDateTime(m.createdAt)} · ${m.sizeBytes.toLocaleString()} bytes`} />
      <div className="row"><StatusBadge status={m.status} />{m.nhi && <span>NHI {m.nhi}</span>}{m.patientId && <Link to={`/patients/${m.patientId}`}>Open patient</Link>}{m.reference && <span>Ref {m.reference}</span>}{m.ackCode && <span>ACK {m.ackCode} {m.ackText}</span>}</div>
      {m.lastError && <p className="finding error"><strong>Why: </strong>{m.lastError}</p>}
      <div className="split">
        <section className="card stack"><h2>What happened</h2>
          <ol className="timeline">{d.events.map((e: any) => <li key={e.id}><span className="small muted">{nzDateTime(e.at)}</span> <strong>{e.step}</strong>{e.detail && <div className="small">{e.detail}</div>}</li>)}</ol>
          {d.related.length > 0 && <><h3>Linked messages</h3><ul className="plain">{d.related.map((r: any) => <li key={r.id}><Link to={`/messaging/messages/${r.id}`}>{r.direction === 'Inbound' ? '⇢' : '⇢ out'} {r.interface}</Link> <StatusBadge status={r.status} /></li>)}</ul></>}
          <Explain id={m.id} canAi={can(Perm.Integration)} />
          {can(Perm.Integration) && m.status !== 'Queued' && m.status !== 'Sending' && <div className="stack-sm"><h3>{m.direction === 'Inbound' ? 'Process again' : 'Send again'}</h3>
            <input aria-label="Reason" value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Reason, e.g. mapping fixed" />
            <div><button className="btn btn-light" onClick={replay} disabled={reason.trim().length < 5}>{m.direction === 'Inbound' ? 'Replay message' : 'Resend message'}</button></div>
            <Errors list={errors} /></div>}
        </section>
        <section className="card stack"><div className="row-between"><h2>Content</h2><div className="tabs compact-tabs"><button className={view === 'tree' ? 'on' : ''} onClick={() => setView('tree')}>Readable</button><button className={view === 'raw' ? 'on' : ''} onClick={() => setView('raw')}>Raw</button></div></div>
          {view === 'tree' ? <TreeView nodes={d.tree} draggable={false} /> : <pre className="mono-box raw">{d.content.replaceAll('\r', '\n')}</pre>}
        </section>
      </div>
    </div>
  );
}

function Explain({ id, canAi }: { id: string; canAi: boolean }) {
  const [findings, setFindings] = useState<any[] | null>(null);
  const [preview, setPreview] = useState<any>(null);
  const [answer, setAnswer] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [errors, setErrors] = useState<string[] | null>(null);
  useEffect(() => { setFindings(null); setPreview(null); setAnswer(null); api<any[]>(`/api/messaging/messages/${id}/explain`).then(setFindings).catch(() => setFindings([])); }, [id]);
  const ask = async () => { setBusy(true); setErrors(null); try { setAnswer((await api(`/api/messaging/messages/${id}/ai`, { body: {} })).answer); } catch (x) { setErrors(errs(x)); } finally { setBusy(false); } };
  return (
    <div className="stack-sm">
      <h3>Why? (built-in check – nothing leaves BrightPAS)</h3>
      {!findings ? <p className="muted">Checking…</p> : <ul className="plain findings">{findings.map((f, i) => <li key={i} className={`finding ${f.severity}`}><strong>{f.title}</strong><div className="small">{f.advice}</div></li>)}</ul>}
      {canAi && !preview && <div><button className="btn btn-light btn-small" onClick={async () => { try { setPreview(await api(`/api/messaging/messages/${id}/ai-preview`)); } catch (x) { setErrors(errs(x)); } }}>Ask the AI assistant…</button></div>}
      {preview && <div className="card subtle stack-sm">
        {!preview.enabled ? <p className="hint">The AI assistant is switched off. An administrator can switch it on in Messaging › Settings once the privacy assessment covers it.</p> : <>
          <p className="hint">This de-identified summary is all that would be sent: patient values are replaced by their shape (A(4) = four letters, 9(8) = eight digits), NHIs and numbers are masked. Check it before sending.</p>
          <pre className="mono-box raw">{preview.payload}</pre>
          <div className="row"><button className="btn btn-primary btn-small" onClick={ask} disabled={busy}>{busy ? 'Asking…' : 'Send to the AI assistant'}</button><button className="btn btn-light btn-small" onClick={() => setPreview(null)}>Cancel</button></div></>}
        {answer && <div className="ai-answer"><strong>Assistant</strong><p className="pre-line">{answer}</p><p className="hint">AI suggestions can be wrong. Check before changing a mapping; changes still need a second person's approval.</p></div>}
      </div>}
      <Errors list={errors} />
    </div>
  );
}

// ---------------------------------------------------------------- translation tables
export function MessagingTables() {
  const { can } = useSession();
  const [list, setList] = useState<any[] | null>(null);
  const [edit, setEdit] = useState<any>(null);
  const [errors, setErrors] = useState<string[] | null>(null);
  const [filter, setFilter] = useState('');
  const load = () => api<any[]>('/api/messaging/tables').then(setList).catch((e) => setErrors(errs(e)));
  useEffect(() => { load(); }, []);
  const open = async (id: string) => { const t = await api(`/api/messaging/tables/${id}`); setEdit({ ...t.table, entries: t.entries }); };
  const save = async () => { setErrors(null); try { await api('/api/messaging/tables', { body: edit }); setEdit(null); load(); } catch (x) { setErrors(errs(x)); } };
  const importCsv = async (f?: File) => {
    if (!f) return;
    try { const entries = await api<any[]>('/api/messaging/tables/parse-csv', { body: { csv: await f.text() } }); setEdit({ ...edit, entries }); } catch (x) { setErrors(errs(x)); }
  };
  const shown = useMemo(() => (edit?.entries ?? []).map((e: any, i: number) => ({ e, i })).filter(({ e }: any) => !filter || `${e.from} ${e.to} ${e.description ?? ''}`.toLowerCase().includes(filter.toLowerCase())), [edit, filter]);
  const setEntry = (i: number, v: any) => setEdit({ ...edit, entries: edit.entries.map((x: any, j: number) => (j === i ? { ...x, ...v } : x)) });
  return (
    <div className="stack-lg">
      <Head title="Translation tables" sub="Translate codes between systems: sex codes, ethnicity, procedure names, funder codes, device rooms." />
      <Errors list={errors} />
      {can(Perm.Integration) && !edit && <div><button className="btn btn-primary" onClick={() => setEdit({ id: null, name: '', description: '', whenMissing: 'PassThrough', defaultValue: '', entries: [{ from: '', to: '', description: '' }] })}>New table</button></div>}
      {edit && (
        <Modal wide title={edit.id ? edit.name : 'New translation table'} onClose={() => { setEdit(null); setErrors(null); }}>
          <div className="form-grid">
            <label>Name<input value={edit.name} onChange={(e) => setEdit({ ...edit, name: e.target.value })} /></label>
            <label>Description<input value={edit.description ?? ''} onChange={(e) => setEdit({ ...edit, description: e.target.value })} /></label>
            <label>When a code is not in the table<select value={edit.whenMissing} onChange={(e) => setEdit({ ...edit, whenMissing: e.target.value })}>
              <option value="PassThrough">Pass it through unchanged (warn)</option><option value="Blank">Leave the field blank (warn)</option><option value="UseDefault">Use a default value (warn)</option><option value="Reject">Stop – the message fails</option></select></label>
            {edit.whenMissing === 'UseDefault' && <label>Default value<input value={edit.defaultValue ?? ''} onChange={(e) => setEdit({ ...edit, defaultValue: e.target.value })} /></label>}
          </div>
          <div className="row"><input aria-label="Search codes" placeholder="Search codes" value={filter} onChange={(e) => setFilter(e.target.value)} />
            <label className="btn btn-light btn-small file-btn">Import CSV (From,To,Description)<input type="file" accept=".csv,text/csv" className="sr-only" onChange={(e) => { importCsv(e.target.files?.[0]); e.target.value = ''; }} /></label>
            {edit.id && <a className="btn btn-light btn-small" href={`/api/messaging/tables/${edit.id}/csv`}>Export CSV</a>}</div>
          <table className="results compact"><thead><tr><th>From</th><th>To</th><th>Description</th><th /></tr></thead>
            <tbody>{shown.slice(0, 500).map(({ e, i }: any) => (
              <tr key={i}><td><input aria-label="From" value={e.from} onChange={(x) => setEntry(i, { from: x.target.value })} /></td><td><input aria-label="To" value={e.to} onChange={(x) => setEntry(i, { to: x.target.value })} /></td>
                <td><input aria-label="Description" value={e.description ?? ''} onChange={(x) => setEntry(i, { description: x.target.value })} /></td>
                <td><button className="btn btn-link" onClick={() => setEdit({ ...edit, entries: edit.entries.filter((_: any, j: number) => j !== i) })}>✕</button></td></tr>))}</tbody></table>
          {shown.length > 500 && <p className="hint">Showing 500 of {shown.length} – search to narrow.</p>}
          <div className="row"><button className="btn btn-light btn-small" onClick={() => setEdit({ ...edit, entries: [...edit.entries, { from: '', to: '', description: '' }] })}>+ Code</button></div>
          <div className="row"><button className="btn btn-primary" onClick={save}>Save</button><button className="btn btn-light" onClick={() => setEdit(null)}>Cancel</button>
            {edit.id && <button className="btn btn-link danger" onClick={async () => { try { await api(`/api/messaging/tables/${edit.id}/delete`, { body: {} }); setEdit(null); load(); } catch (x) { setErrors(errs(x)); } }}>Delete table</button>}</div>
        </Modal>)}
      {list && <table className="results"><thead><tr><th>Table</th><th>Codes</th><th>Missing codes</th><th>Used by</th><th>Changed</th></tr></thead>
        <tbody>{list.map((t) => <tr key={t.id}><td><button className="btn btn-link" onClick={() => open(t.id)}>{t.name}</button><div className="small muted">{t.description}</div></td><td>{t.entries}</td><td>{t.whenMissing}</td><td>{t.usedByRules} rule(s)</td><td className="small">{nzDateTime(t.updatedAt)} {t.updatedBy}</td></tr>)}</tbody></table>}
    </div>
  );
}

// ---------------------------------------------------------------- settings
export function MessagingSettings() {
  const [s, setS] = useState<any>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const [errors, setErrors] = useState<string[] | null>(null);
  useEffect(() => { api('/api/messaging/settings').then(setS).catch((e) => setErrors(errs(e))); }, []);
  const save = async () => { setErrors(null); setMsg(null); try { await api('/api/messaging/settings', { method: 'PUT', body: { ...s, maxAttempts: Number(s.maxAttempts) } }); setMsg('Saved.'); } catch (x) { setErrors(errs(x)); } };
  return (
    <div className="stack-lg">
      <Head title="Messaging settings" />
      <Errors list={errors} />
      {s && <section className="card stack">
        <label>Attempts before a message gives up (retries wait 1, 2, 5, 10, 30, 60, 120, 240 minutes)<input type="number" min={1} max={20} value={s.maxAttempts} onChange={(e) => setS({ ...s, maxAttempts: e.target.value })} /></label>
        <label>Email alerts to (support contact)<input type="email" value={s.alertEmail ?? ''} onChange={(e) => setS({ ...s, alertEmail: e.target.value })} placeholder="it-support@…" /></label>
        <p className="hint">Alert emails contain no patient details. They use the SMS & email reminders module's email sender.</p>
        <label className="check"><input type="checkbox" checked={s.requireSecondApprover} onChange={(e) => setS({ ...s, requireSecondApprover: e.target.checked })} /> A second person must approve mapping changes before they go live (recommended)</label>
        <div><button className="btn btn-primary" onClick={save}>Save</button></div>
        {msg && <p className="ok-text">{msg}</p>}
      </section>}
      <AiSettings />
      <McpClients />
    </div>
  );
}

function AiSettings() {
  const [s, setS] = useState<any>(null);
  const [key, setKey] = useState('');
  const [confirmed, setConfirmed] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);
  const [errors, setErrors] = useState<string[] | null>(null);
  useEffect(() => { api('/api/messaging/ai').then(setS).catch(() => setS(null)); }, []);
  const save = async () => {
    setErrors(null); setMsg(null);
    try { await api('/api/messaging/ai', { method: 'PUT', body: { enabled: s.enabled, endpoint: s.endpoint, model: s.model, apiKey: key || null, privacyReference: s.privacyReference, privacyConfirmed: confirmed } }); setKey(''); setMsg('Saved.'); setS(await api('/api/messaging/ai')); }
    catch (x) { setErrors(errs(x)); }
  };
  if (!s) return null;
  return (
    <section className="card stack">
      <h2>AI troubleshooting assistant</h2>
      <p className="hint">Off by default. When on, integration administrators can ask an AI service to explain a failed message. Only a de-identified summary is sent (message shape without values, redacted errors, flow and mapping rules) and each request is shown first and recorded in the audit log. The AI service is outside TDS, so the privacy impact assessment must cover this use before it is switched on.</p>
      <label className="check"><input type="checkbox" checked={s.enabled} onChange={(e) => setS({ ...s, enabled: e.target.checked })} /> Switched on</label>
      <div className="form-grid">
        <label>AI service address<input value={s.endpoint} onChange={(e) => setS({ ...s, endpoint: e.target.value })} /></label>
        <label>Model<input value={s.model} onChange={(e) => setS({ ...s, model: e.target.value })} /></label>
        <label>API key (stored encrypted){s.hasKey && <span className="muted small"> – one is saved; leave blank to keep</span>}<input type="password" value={key} onChange={(e) => setKey(e.target.value)} autoComplete="off" /></label>
        <label>Privacy assessment reference<input value={s.privacyReference ?? ''} onChange={(e) => setS({ ...s, privacyReference: e.target.value })} placeholder="e.g. PIA-2026-07 approved by the Privacy Officer" /></label>
      </div>
      {s.enabled && <label className="check"><input type="checkbox" checked={confirmed} onChange={(e) => setConfirmed(e.target.checked)} /> I confirm the privacy impact assessment covers sending de-identified interface summaries to this AI service.</label>}
      {s.approvedBy && <p className="small muted">Switched on by {s.approvedBy} on {nzDateTime(s.approvedAt)}.</p>}
      <div><button className="btn btn-primary" onClick={save}>Save AI settings</button></div>
      <Errors list={errors} />{msg && <p className="ok-text">{msg}</p>}
    </section>
  );
}

function McpClients() {
  const [list, setList] = useState<any[] | null>(null);
  const [f, setF] = useState({ name: '', days: 90 });
  const [token, setToken] = useState<string | null>(null);
  const [errors, setErrors] = useState<string[] | null>(null);
  const load = () => api<any[]>('/api/messaging/mcp-clients').then(setList).catch(() => setList(null));
  useEffect(() => { load(); }, []);
  const add = async () => { setErrors(null); try { setToken((await api('/api/messaging/mcp-clients', { body: { ...f, days: Number(f.days) } })).token); setF({ name: '', days: 90 }); load(); } catch (x) { setErrors(errs(x)); } };
  if (!list) return null;
  return (
    <section className="card stack">
      <h2>AI tool access (MCP)</h2>
      <p className="hint">Lets an AI tool (e.g. Claude) read the engine's health over the Model Context Protocol at <code>https://{window.location.host}/api/mcp</code>: interface status, failed messages with redacted errors, flows and built-in explanations. It can never see message content or patient identifiers, cannot change anything, and every call is audited.</p>
      <div className="row"><label>Tool name<input value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} placeholder="e.g. Claude – IT support" /></label>
        <label>Valid for (days)<input type="number" min={1} max={365} value={f.days} onChange={(e) => setF({ ...f, days: Number(e.target.value) })} /></label>
        <button className="btn btn-light" onClick={add}>Create access token</button></div>
      {token && <div className="copy-box"><p><strong>Copy this token now</strong> – it is shown only once. Use it as a Bearer token in the AI tool's MCP settings:</p><code className="mono-box">{token}</code></div>}
      <Errors list={errors} />
      {list.length > 0 && <table className="results compact"><thead><tr><th>Tool</th><th>Created</th><th>Expires</th><th>Last used</th><th /></tr></thead>
        <tbody>{list.map((c) => <tr key={c.id}><td>{c.name}{!c.isActive && <span className="badge off"> revoked</span>}</td><td className="small">{nzDateTime(c.createdAt)} {c.createdBy}</td><td className="small">{nzDateTime(c.expiresAt)}</td><td className="small">{nzDateTime(c.lastUsedAt)}</td>
          <td>{c.isActive && <button className="btn btn-link danger" onClick={async () => { await api(`/api/messaging/mcp-clients/${c.id}/revoke`, { body: {} }); load(); }}>Revoke</button>}</td></tr>)}</tbody></table>}
    </section>
  );
}

/** Reads a DICOM file in the browser and asks the server for its header as DICOM JSON (for samples). */
export async function dicomFileToJson(f: File): Promise<string> {
  const r = await api('/api/messaging/dicom/convert', { body: { contentBase64: await fileToBase64(f) } });
  return r.json;
}

export type { DocNode };

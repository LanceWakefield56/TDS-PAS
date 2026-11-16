import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router';
import { api, errText, formatDate, nzDateTime, Perm, fileToText } from '../api';
import FileImport from '../components/FileImport';
import { useSession } from '../App';

const TRAY_PILL: Record<string, string> = { Sterile: 'pill green', ExpiringSoon: 'pill', Expired: 'pill bad', Quarantine: 'pill bad', Dirty: 'pill', Washed: 'pill', Packed: 'pill', Issued: 'pill' };
const SCOPE_PILL: Record<string, string> = { Ready: 'pill green', NeedsReprocessing: 'pill', StorageExpired: 'pill bad', Quarantined: 'pill bad', OutOfService: 'pill bad' };
const KIND: Record<string, string> = { Wash: 'Washer', Steam: 'Steam steriliser', LowTemperature: 'Low-temperature steriliser', Aer: 'Endoscope reprocessor (AER)' };
const IND = ['NotUsed', 'Pending', 'Pass', 'Fail'];
const INDL: Record<string, string> = { NotUsed: 'Not used', Pending: 'Waiting for result', Pass: 'Pass', Fail: 'Fail' };

/** Shared error/message handling for the CSSD tabs. */
function useAct() {
  const [error, setError] = useState<string | null>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const act = async (fn: () => Promise<any>, ok?: string | ((r: any) => string)) => {
    setError(null); setMsg(null);
    try { const r = await fn(); if (ok) setMsg(typeof ok === 'function' ? ok(r) : ok); return r ?? true; } catch (x: any) { setError(x?.body?.errors?.join(' ') ?? errText(x)); return null; }
  };
  const view = <>{error && <p className="error" role="alert">{error}</p>}{msg && <p className="ok-text" role="status">{msg}</p>}</>;
  return { act, view };
}

/** Sterile services (CSSD): today's work, loads, trays, endoscopes, loan sets, nonconformance and traceability. */
export default function Cssd() {
  const [params, setParams] = useSearchParams();
  const tab = params.get('tab') ?? 'today';
  const tabs: [string, string][] = [['today', 'Today'], ['loads', 'Loads'], ['trays', 'Trays'], ['scopes', 'Endoscopes'], ['loans', 'Loan sets'], ['nc', 'Nonconformance'], ['trace', 'Trace & recall']];
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Sterile services (CSSD)</h1><p className="muted">Trays, washer and steriliser loads, endoscopes and traceability to AS/NZS 5369:2023.</p></div>
      <section className="card stack">
        <div className="tabs" role="tablist">{tabs.map(([k, t]) => <button key={k} role="tab" aria-selected={tab === k} className={tab === k ? 'on' : ''} onClick={() => setParams({ tab: k })}>{t}</button>)}</div>
        {tab === 'today' && <Today />}
        {tab === 'loads' && <Loads />}
        {tab === 'trays' && <Trays />}
        {tab === 'scopes' && <Scopes />}
        {tab === 'loans' && <Loans />}
        {tab === 'nc' && <Nonconformance />}
        {tab === 'trace' && <Trace />}
      </section>
    </div>
  );
}

function Today() {
  const { can } = useSession();
  const { act, view } = useAct();
  const [d, setD] = useState<any>(null);
  const [cases, setCases] = useState<any[]>([]);
  const [scan, setScan] = useState({ barcode: '', mode: 'return', bookingId: '', checked: false });
  const load = () => { api('/api/assets/dashboard').then(setD).catch(() => undefined); if (can(Perm.CssdProcess)) api<any[]>('/api/assets/cases').then(setCases).catch(() => undefined); };
  useEffect(load, []); // eslint-disable-line react-hooks/exhaustive-deps
  const go = async (e: React.FormEvent) => {
    e.preventDefault();
    const b = scan.barcode.trim();
    if (!b) return;
    const r = scan.mode === 'return' ? await act(() => api('/api/assets/trays/return', { body: { barcode: b } }), `${b} is back in CSSD (dirty).`)
      : scan.mode === 'pack' ? await act(() => api('/api/assets/trays/pack', { body: { barcode: b, contentsChecked: scan.checked } }), `${b} packed – ready for the steriliser.`)
      : await act(() => api('/api/assets/trays/issue', { body: { barcode: b, bookingId: scan.bookingId } }), (x) => `${b} issued to ${cases.find((c) => c.bookingId === scan.bookingId)?.patientName} (load ${x.loadNumber}).`);
    if (r) { setScan({ ...scan, barcode: '', checked: false }); load(); }
  };
  const c = d?.cssd;
  return (
    <div className="stack">
      {c && <div className="stat-row">
        {[['Dirty', c.dirty], ['Washed', c.washed], ['Packed', c.packed], ['Sterile', c.sterile], ['In theatre', c.issued], ['Quarantine', c.quarantine]].map(([t, n]) =>
          <div key={t as string} className="stat"><strong>{n}</strong><span>{t}</span></div>)}
      </div>}
      {c && <ul className="plain">
        {c.loadsAwaitingRelease > 0 && <li>⚠ <strong>{c.loadsAwaitingRelease}</strong> load(s) waiting for release (Loads)</li>}
        {c.biPending > 0 && <li><strong>{c.biPending}</strong> biological indicator result(s) to record</li>}
        {c.expired > 0 && <li className="error-text"><strong>{c.expired}</strong> tray(s) past their storage date – reprocess</li>}
        {c.expiringSoon > 0 && <li><strong>{c.expiringSoon}</strong> tray(s) reach their storage date within 7 days</li>}
        {c.scopesNotReady > 0 && <li><strong>{c.scopesNotReady}</strong> endoscope(s) not ready (Endoscopes)</li>}
        {c.openNonconformances > 0 && <li><strong>{c.openNonconformances}</strong> open nonconformance(s)</li>}
        {d.maintenanceOverdue > 0 && <li className="error-text"><strong>{d.maintenanceOverdue}</strong> equipment check(s) overdue (Assets)</li>}
        {d.lockedOut.length > 0 && <li>Out of service: {d.lockedOut.map((e: any) => `${e.name} (${e.outOfServiceReason})`).join('; ')}</li>}
      </ul>}
      {can(Perm.CssdProcess) && <form className="card subtle stack" onSubmit={go}>
        <h2>Scan a tray</h2>
        <div className="row" role="radiogroup" aria-label="What are you doing?">
          {[['return', 'Back from theatre (dirty)'], ['pack', 'Inspect & pack'], ['issue', 'Issue to a case']].map(([k, t]) =>
            <label key={k} className="check"><input type="radio" name="scanmode" checked={scan.mode === k} onChange={() => setScan({ ...scan, mode: k })} /> {t}</label>)}
        </div>
        <div className="form-grid">
          <label>Tray barcode<input value={scan.barcode} onChange={(e) => setScan({ ...scan, barcode: e.target.value })} autoComplete="off" placeholder="Scan or type, e.g. LAPCHOLE-01" /></label>
          {scan.mode === 'issue' && <label>Case<select value={scan.bookingId} onChange={(e) => setScan({ ...scan, bookingId: e.target.value })}>
            <option value="">Choose today's case…</option>{cases.map((x) => <option key={x.bookingId} value={x.bookingId}>{x.start?.slice(0, 5)} {x.patientName} – {x.procedure} ({x.locationName})</option>)}</select></label>}
          {scan.mode === 'pack' && <label className="check"><input type="checkbox" checked={scan.checked} onChange={(e) => setScan({ ...scan, checked: e.target.checked })} /> Every item on the contents list is present, clean and working</label>}
        </div>
        <div><button className="btn btn-primary">{scan.mode === 'return' ? 'Record return' : scan.mode === 'pack' ? 'Record packed' : 'Issue tray'}</button></div>
        {view}
      </form>}
    </div>
  );
}

function Loads() {
  const { can } = useSession();
  const { act, view } = useAct();
  const [rows, setRows] = useState<any[] | null>(null);
  const [machines, setMachines] = useState<any[]>([]);
  const [pending, setPending] = useState(true);
  const [form, setForm] = useState<any>(null);
  const [sel, setSel] = useState<any>(null);
  const [fail, setFail] = useState('');
  const load = () => api<any[]>(`/api/assets/cycles${pending ? '?view=pending' : ''}`).then(setRows).catch(() => setRows([]));
  useEffect(() => { load(); api<any[]>('/api/assets/equipment').then((e) => setMachines(e.filter((x) => ['Washer', 'Steriliser', 'Aer'].includes(x.category)))).catch(() => undefined); }, [pending]); // eslint-disable-line react-hooks/exhaustive-deps
  const now = () => { const d = new Date(); d.setMinutes(d.getMinutes() - d.getTimezoneOffset()); return d.toISOString().slice(0, 16); };
  const blank = (kind: string) => ({ kind, machineId: machines.find((m) => (kind === 'Wash' ? m.category === 'Washer' : kind === 'Aer' ? m.category === 'Aer' : m.category === 'Steriliser'))?.id ?? '', machineCycleNumber: '', program: '',
    startedAt: now(), endedAt: '', maxTemperatureC: '', holdSeconds: '', pressureKpa: '', a0: '', machinePassed: true, chemicalIndicator: kind === 'Wash' ? 'NotUsed' : 'Pass', biologicalIndicator: 'NotUsed', bowieDickTest: false,
    trays: '', others: '', logText: null as string | null, fileName: '' });
  const num = (v: any) => v === '' || v === null || v === undefined ? null : Number(v);
  const lines = (s: string) => s.split(/[\n,]+/).map((x) => x.trim()).filter(Boolean);
  const others = (s: string) => s.split('\n').map((x) => x.trim()).filter(Boolean).map((x) => { const m = x.match(/^(\d+)\s*x?\s+(.*)$/i); return m ? { quantity: Number(m[1]), description: m[2] } : { quantity: 1, description: x }; });
  const save = async () => {
    const common = { machineId: form.machineId, kind: form.kind, trayBarcodes: lines(form.trays), otherItems: others(form.others), chemicalIndicator: form.chemicalIndicator, biologicalIndicator: form.biologicalIndicator, bowieDickTest: form.bowieDickTest };
    const r = form.logText
      ? await act(() => api('/api/assets/cycles/import', { body: { ...common, fileName: form.fileName, text: form.logText, startedAt: form.startedAt ? new Date(form.startedAt).toISOString() : null } }), (x) => `Load ${x.loadNumber} imported from ${form.fileName}.${x.parsed.notes.length ? ' Check: ' + x.parsed.notes.join(' ') : ''}`)
      : await act(() => api('/api/assets/cycles', { body: { ...common, machineCycleNumber: form.machineCycleNumber || null, program: form.program || null, startedAt: new Date(form.startedAt).toISOString(),
        endedAt: form.endedAt ? new Date(form.endedAt).toISOString() : null, maxTemperatureC: num(form.maxTemperatureC), holdSeconds: num(form.holdSeconds), pressureKpa: num(form.pressureKpa), a0: num(form.a0), machinePassed: form.machinePassed } }), (x) => `Load ${x.loadNumber} recorded.`);
    if (r) { setForm(null); load(); }
  };
  const readLog = async (f: File | undefined) => {
    if (!f) return;
    const text = await fileToText(f);
    const p = await act(() => api('/api/assets/cycles/parse', { body: { text } }));
    if (p) setForm({ ...form, logText: text, fileName: f.name, machineCycleNumber: p.machineCycleNumber ?? '', program: p.program ?? '', maxTemperatureC: p.maxTemperatureC ?? '', holdSeconds: p.holdSeconds ?? '', a0: p.a0 ?? '',
      pressureKpa: p.pressureKpa ?? '', machinePassed: p.passed === true, startedAt: p.startedAt ? p.startedAt.slice(0, 16) : form.startedAt, parseNotes: p.notes });
  };
  const [contents, setContents] = useState({ trays: '', others: '' });
  const addContents = async () => {
    if (await act(() => api(`/api/assets/cycles/${sel.cycle.id}/contents`, { body: { trayBarcodes: lines(contents.trays), otherItems: others(contents.others) } }), (x) => x.message)) {
      setContents({ trays: '', others: '' }); setSel(await api(`/api/assets/cycles/${sel.cycle.id}`));
    }
  };
  const open = async (r: any) => { setFail(''); setSel(await api(`/api/assets/cycles/${r.id}`)); };
  const release = async (ok: boolean) => { if (await act(() => api(`/api/assets/cycles/${sel.cycle.id}/release`, { body: { release: ok, note: ok ? null : fail } }), (x) => x.message)) { setSel(null); load(); } };
  const indicators = async (ci: string, bi: string) => { if (await act(() => api(`/api/assets/cycles/${sel.cycle.id}/indicators`, { body: { chemical: ci, biological: bi } }), (x) => x?.warning ?? 'Indicator results recorded.')) { await open(sel.cycle); load(); } };
  return (
    <div className="stack">
      <div className="row">
        <label className="check"><input type="checkbox" checked={pending} onChange={(e) => setPending(e.target.checked)} /> Only loads waiting for release</label>
        {can(Perm.CssdProcess) && !form && <>{['Wash', 'Steam', 'LowTemperature', 'Aer'].map((k) => <button key={k} className="btn btn-light" onClick={() => setForm(blank(k))}>New {KIND[k].toLowerCase()} load</button>)}</>}
      </div>
      {view}
      {can(Perm.CssdProcess) && !form && <FileImport url="/api/assets/cycles/import-files" title="Cycle records from the steriliser or washer (USB / log folder)" accept=".csv,.txt,.log,.dat,.zip" onDone={load}
        hint="Choose the cycle record files copied from the machine (or a ZIP). Each becomes a load waiting for release, with the full temperature and pressure curve and BrightPAS's plateau checks; a cycle already imported is skipped. The record must name the machine as it is in the equipment register (e.g. Sterilizer 1)." />}
      {can(Perm.AssetsManage) && !form && <LogFolder />}
      {form && <div className="card subtle stack">
        <h2>New {KIND[form.kind].toLowerCase()} load</h2>
        <label className="btn btn-light file-btn">Read the machine's log file (Belimed etc.)<input type="file" className="sr-only" accept=".txt,.csv,.log,.dat,text/*" onChange={(e) => { readLog(e.target.files?.[0]); e.target.value = ''; }} /></label>
        {form.fileName && <p className="hint">From {form.fileName}. {form.parseNotes?.length ? 'Check: ' + form.parseNotes.join(' ') : 'All values read – check them against the printout.'}</p>}
        <div className="form-grid">
          <label>Machine<select value={form.machineId} onChange={(e) => setForm({ ...form, machineId: e.target.value })}><option value="">Choose…</option>{machines.map((m) => <option key={m.id} value={m.id} disabled={m.status !== 'InService'}>{m.name} ({m.assetNumber}){m.status !== 'InService' ? ' – out of service' : ''}</option>)}</select></label>
          <label>Machine cycle / batch number<input value={form.machineCycleNumber} onChange={(e) => setForm({ ...form, machineCycleNumber: e.target.value })} /></label>
          <label>Program<input value={form.program} onChange={(e) => setForm({ ...form, program: e.target.value })} placeholder={form.kind === 'Wash' ? 'e.g. Instruments' : 'e.g. 134 °C 4 min'} /></label>
          <label>Started<input type="datetime-local" value={form.startedAt} onChange={(e) => setForm({ ...form, startedAt: e.target.value })} /></label>
          <label>Finished<input type="datetime-local" value={form.endedAt} onChange={(e) => setForm({ ...form, endedAt: e.target.value })} /></label>
          <label>Highest temperature (°C)<input type="number" step="0.1" value={form.maxTemperatureC} onChange={(e) => setForm({ ...form, maxTemperatureC: e.target.value })} /></label>
          <label>Holding time (seconds)<input type="number" value={form.holdSeconds} onChange={(e) => setForm({ ...form, holdSeconds: e.target.value })} /></label>
          {form.kind !== 'Wash' && <label>Pressure (kPa)<input type="number" value={form.pressureKpa} onChange={(e) => setForm({ ...form, pressureKpa: e.target.value })} /></label>}
          {form.kind === 'Wash' && <label>A0 value<input type="number" value={form.a0} onChange={(e) => setForm({ ...form, a0: e.target.value })} /></label>}
          <label>Chemical indicator<select value={form.chemicalIndicator} onChange={(e) => setForm({ ...form, chemicalIndicator: e.target.value })}>{IND.map((i) => <option key={i} value={i}>{INDL[i]}</option>)}</select></label>
          {form.kind !== 'Wash' && <label>Biological indicator<select value={form.biologicalIndicator} onChange={(e) => setForm({ ...form, biologicalIndicator: e.target.value })}>{IND.map((i) => <option key={i} value={i}>{INDL[i]}</option>)}</select></label>}
          {form.kind === 'Steam' && <label className="check"><input type="checkbox" checked={form.bowieDickTest} onChange={(e) => setForm({ ...form, bowieDickTest: e.target.checked })} /> Bowie-Dick test in this load</label>}
          <label className="check"><input type="checkbox" checked={form.machinePassed} onChange={(e) => setForm({ ...form, machinePassed: e.target.checked })} /> The machine reported a good cycle (no faults)</label>
        </div>
        <div className="form-grid">
          <label>Trays in the load (scan each barcode, one per line)<textarea rows={4} value={form.trays} onChange={(e) => setForm({ ...form, trays: e.target.value })} /></label>
          <label>Other items (one per line, e.g. "6 steri packs")<textarea rows={4} value={form.others} onChange={(e) => setForm({ ...form, others: e.target.value })} /></label>
        </div>
        <div className="row"><button className="btn btn-primary" onClick={save}>Save load</button><button className="btn btn-light" onClick={() => setForm(null)}>Cancel</button></div>
      </div>}
      {!rows ? <p>Loading…</p> : rows.length === 0 ? <p className="muted">{pending ? 'No loads waiting for release.' : 'No loads recorded.'}</p> : (
        <table className="results"><thead><tr><th>Load</th><th>Machine</th><th>Started</th><th>Result</th><th>Indicators</th><th>Contents</th><th>Release</th></tr></thead>
          <tbody>{rows.map((r) => <tr key={r.id} className={sel?.cycle?.id === r.id ? 'selected' : ''}>
            <td><button className="btn btn-link" onClick={() => open(r)}>{r.loadNumber}</button><div className="small muted">{KIND[r.kind]}{r.program ? ` · ${r.program}` : ''}</div></td>
            <td>{r.machine}</td><td>{nzDateTime(r.startedAt)}</td>
            <td>{r.machinePassed ? <span className="pill green">Machine OK</span> : <span className="pill bad">Fault</span>}<div className="small muted">{r.maxTemperatureC != null && `${r.maxTemperatureC} °C`}{r.a0 != null && ` · A0 ${r.a0}`}</div></td>
            <td className="small">CI {INDL[r.chemicalIndicator]}{r.biologicalIndicator !== 'NotUsed' && <><br />BI {INDL[r.biologicalIndicator]}</>}</td>
            <td className="small">{r.trays} tray(s){r.otherItems > 0 && `, ${r.otherItems} other`}</td>
            <td>{r.release === 'Pending' ? <span className="pill">Waiting</span> : r.release === 'Released' ? <span className="pill green">Released</span> : <span className="pill bad">Failed</span>}<div className="small muted">{r.releasedBy}</div></td></tr>)}</tbody></table>)}
      {sel && <div className="card subtle stack">
        <h2>Load {sel.cycle.loadNumber}</h2>
        <p>{sel.cycle.machine} · {KIND[sel.cycle.kind]} · started {nzDateTime(sel.cycle.startedAt)} by {sel.cycle.operator} · source {sel.cycle.source}</p>
        {sel.trays.length > 0 && <p>Trays: {sel.trays.map((t: any) => `${t.barcode} (${t.name})`).join(', ')}</p>}
        {sel.others.length > 0 && <p>Other: {sel.others.map((o: any) => `${o.quantity} × ${o.description}`).join(', ')}</p>}
        {sel.cycle.release === 'Pending' && can(Perm.CssdProcess) && sel.cycle.kind !== 'Aer' && <details className="card subtle" open={sel.trays.length + sel.others.length === 0}>
          <summary>{sel.trays.length + sel.others.length === 0 ? 'Contents not listed yet – scan the trays in this load' : 'Add more trays or items to this load'}</summary>
          <div className="row"><label>Tray barcodes (scan, one per line)<textarea rows={3} value={contents.trays} onChange={(e) => setContents({ ...contents, trays: e.target.value })} /></label>
            <label>Other items (e.g. "4 x steri pack")<textarea rows={3} value={contents.others} onChange={(e) => setContents({ ...contents, others: e.target.value })} /></label></div>
          <button className="btn btn-light" onClick={addContents}>Add to load</button>
        </details>}
        {sel.steriliser && <SteriliserCurve r={sel.steriliser} />}
        {sel.rawLog && <details><summary>Machine log</summary><pre>{sel.rawLog}</pre></details>}
        {can(Perm.CssdProcess) && <div className="row">
          <label>Chemical indicator<select value={sel.cycle.chemicalIndicator} onChange={(e) => indicators(e.target.value, sel.cycle.biologicalIndicator)}>{IND.map((i) => <option key={i} value={i}>{INDL[i]}</option>)}</select></label>
          <label>Biological indicator<select value={sel.cycle.biologicalIndicator} onChange={(e) => indicators(sel.cycle.chemicalIndicator, e.target.value)}>{IND.map((i) => <option key={i} value={i}>{INDL[i]}</option>)}</select></label>
        </div>}
        {sel.cycle.release === 'Pending' && can(Perm.CssdRelease) && <div className="stack">
          <div><button className="btn btn-primary" onClick={() => release(true)}>Release load (all parameters met)</button></div>
          <div className="row"><label>If the load failed, why?<input value={fail} onChange={(e) => setFail(e.target.value)} placeholder="e.g. chemical indicator did not change" /></label><button className="btn btn-warn" onClick={() => release(false)}>Fail load</button></div>
        </div>}
        {sel.cycle.releaseNote && <p>Note: {sel.cycle.releaseNote}</p>}
      </div>}
    </div>
  );
}

function Trays() {
  const { can } = useSession();
  const { act, view } = useAct();
  const [sets, setSets] = useState<any[]>([]);
  const [trays, setTrays] = useState<any[] | null>(null);
  const [status, setStatus] = useState('');
  const [edit, setEdit] = useState<any>(null);
  const load = () => { api<any[]>('/api/assets/tray-sets?all=true').then(setSets).catch(() => undefined); api<any[]>(`/api/assets/trays${status ? `?status=${status}` : ''}`).then(setTrays).catch(() => setTrays([])); };
  useEffect(load, [status]); // eslint-disable-line react-hooks/exhaustive-deps
  const save = async () => {
    const items = edit.itemsText.split('\n').map((x: string) => x.trim()).filter(Boolean).map((x: string) => { const m = x.match(/^(\d+)\s*x?\s+(.*)$/i); return m ? { quantity: Number(m[1]), description: m[2] } : { quantity: 1, description: x }; });
    if (await act(() => api('/api/assets/tray-sets', { body: { id: edit.id ?? null, code: edit.code, name: edit.name, specialty: edit.specialty || null, shelfLifeDays: Number(edit.shelfLifeDays), isActive: edit.isActive, items } }), 'Tray saved.')) { setEdit(null); load(); }
  };
  const add = async (s: any) => { const n = window.prompt(`How many physical ${s.name} trays to add?`, '1'); if (n && await act(() => api(`/api/assets/tray-sets/${s.id}/trays`, { body: { count: Number(n) } }), (r) => `Added ${r.map((t: any) => t.barcode).join(', ')} – print their labels.`)) load(); };
  const print = (t: any) => act(() => api(`/api/assets/trays/${t.id}/print`, { body: {} }), `Label for ${t.barcode} sent to the label printer.`);
  return (
    <div className="stack">
      {view}
      <h2>Tray catalogue</h2>
      {sets.length === 0 ? <p className="muted">No trays yet. Add each tray with its contents list (count sheet).</p> : (
        <table className="results compact"><thead><tr><th>Code</th><th>Tray</th><th>Items</th><th>Storage life</th><th></th></tr></thead>
          <tbody>{sets.map((s) => <tr key={s.id}><td>{s.code}</td><td>{s.name}{!s.isActive && ' (inactive)'}<div className="small muted">{s.specialty}</div></td>
            <td className="small">{s.items.length} lines, {s.items.reduce((a: number, i: any) => a + i.quantity, 0)} items</td><td>{s.shelfLifeDays} days</td>
            <td className="actions">{can(Perm.AssetsManage) && <><button className="btn btn-link" onClick={() => setEdit({ ...s, itemsText: s.items.map((i: any) => `${i.quantity} ${i.description}`).join('\n') })}>Edit</button>
              <button className="btn btn-link" onClick={() => add(s)}>Add trays</button></>}</td></tr>)}</tbody></table>)}
      {can(Perm.AssetsManage) && !edit && <div><button className="btn btn-light" onClick={() => setEdit({ code: '', name: '', specialty: '', shelfLifeDays: 90, isActive: true, itemsText: '' })}>New tray</button></div>}
      {edit && <div className="card subtle stack"><div className="form-grid">
        <label>Code<input value={edit.code} onChange={(e) => setEdit({ ...edit, code: e.target.value })} placeholder="e.g. LAPCHOLE" /></label>
        <label>Name<input value={edit.name} onChange={(e) => setEdit({ ...edit, name: e.target.value })} /></label>
        <label>Specialty<input value={edit.specialty ?? ''} onChange={(e) => setEdit({ ...edit, specialty: e.target.value })} /></label>
        <label>Storage life (days)<input type="number" value={edit.shelfLifeDays} onChange={(e) => setEdit({ ...edit, shelfLifeDays: e.target.value })} /></label>
        <label className="check"><input type="checkbox" checked={edit.isActive} onChange={(e) => setEdit({ ...edit, isActive: e.target.checked })} /> In use</label></div>
        <label>Contents list – one item per line, quantity first (e.g. "2 Maryland grasper")<textarea rows={8} value={edit.itemsText} onChange={(e) => setEdit({ ...edit, itemsText: e.target.value })} /></label>
        <div className="row"><button className="btn btn-primary" onClick={save}>Save tray</button><button className="btn btn-light" onClick={() => setEdit(null)}>Cancel</button></div></div>}
      <h2>Barcoded trays</h2>
      <label>Show<select value={status} onChange={(e) => setStatus(e.target.value)}><option value="">All</option>{['Dirty', 'Washed', 'Packed', 'Sterile', 'Issued', 'Quarantine'].map((s) => <option key={s}>{s}</option>)}</select></label>
      {!trays ? <p>Loading…</p> : trays.length === 0 ? <p className="muted">None.</p> : (
        <table className="results compact"><thead><tr><th>Barcode</th><th>Tray</th><th>Status</th><th>Last load</th><th>Use by</th><th></th></tr></thead>
          <tbody>{trays.map((t) => <tr key={t.id}><td>{t.barcode}</td><td>{t.name}</td><td><span className={TRAY_PILL[t.state] ?? 'pill'}>{t.state === 'ExpiringSoon' ? 'Use soon' : t.state}</span></td>
            <td>{t.lastLoad ?? '—'}</td><td>{t.sterileUntil ? formatDate(t.sterileUntil) : '—'}</td>
            <td>{can(Perm.CssdProcess) && t.status === 'Sterile' && <button className="btn btn-link" onClick={() => print(t)}>Print label</button>}</td></tr>)}</tbody></table>)}
    </div>
  );
}

function Scopes() {
  const { can } = useSession();
  const { act, view } = useAct();
  const [rows, setRows] = useState<any[] | null>(null);
  const [cases, setCases] = useState<any[]>([]);
  const [sel, setSel] = useState<any>(null);
  const [history, setHistory] = useState<any[]>([]);
  const [ev, setEv] = useState<any>({ kind: 'LeakTest', passed: 'true', bookingId: '', notes: '' });
  const load = () => api<any[]>('/api/assets/scopes').then(setRows).catch(() => setRows([]));
  useEffect(() => { load(); if (can(Perm.CssdProcess)) api<any[]>('/api/assets/cases').then(setCases).catch(() => undefined); }, []); // eslint-disable-line react-hooks/exhaustive-deps
  const open = async (s: any) => { setSel(s); setHistory(await api(`/api/assets/scopes/${s.id}/history`)); };
  const record = async () => {
    const needsResult = ['LeakTest', 'AerCycle', 'MicroSample'].includes(ev.kind);
    const r = await act(() => api('/api/assets/scopes/events', { body: { scopeId: sel.id, kind: ev.kind, passed: needsResult ? ev.passed === 'true' : null, bookingId: ev.kind === 'Use' ? ev.bookingId || null : null, notes: ev.notes || null } }),
      (x) => x.warning ?? 'Recorded.');
    if (r) { setEv({ ...ev, notes: '' }); load(); await open(sel); }
  };
  return (
    <div className="stack">
      {view}
      {!rows ? <p>Loading…</p> : rows.length === 0 ? <p className="muted">No endoscopes. Add them on the Assets page with category Endoscope.</p> : (
        <table className="results"><thead><tr><th>Scope</th><th>Status</th><th>Last reprocessed</th><th>Last used</th></tr></thead>
          <tbody>{rows.map((s) => <tr key={s.id} className={sel?.id === s.id ? 'selected' : ''}><td><button className="btn btn-link" onClick={() => open(s)}>{s.name}</button><div className="small muted">{s.assetNumber} · {s.model} · serial {s.serialNumber}</div></td>
            <td><span className={SCOPE_PILL[s.state]}>{s.state === 'NeedsReprocessing' ? 'Reprocess' : s.state === 'StorageExpired' ? 'Storage expired' : s.state}</span><div className="small muted">{s.stateText}</div></td>
            <td>{s.lastReprocessed ? nzDateTime(s.lastReprocessed) : '—'}</td><td>{s.lastUsed ? nzDateTime(s.lastUsed) : '—'}</td></tr>)}</tbody></table>)}
      {sel && <div className="card subtle stack">
        <h2>{sel.name} ({sel.assetNumber})</h2>
        {can(Perm.CssdProcess) && <div className="form-grid">
          <label>What happened<select value={ev.kind} onChange={(e) => setEv({ ...ev, kind: e.target.value })}>
            {[['LeakTest', 'Leak test'], ['ManualClean', 'Manual clean'], ['AerCycle', 'AER cycle'], ['DryingIn', 'Into drying cabinet'], ['DryingOut', 'Out of drying cabinet'], ['Use', 'Used on a patient'], ['MicroSample', 'Microbiology sample result'], ['Repair', 'Back from repair'], ['Quarantine', 'Quarantine']].map(([k, t]) => <option key={k} value={k}>{t}</option>)}</select></label>
          {['LeakTest', 'AerCycle', 'MicroSample'].includes(ev.kind) && <label>Result<select value={ev.passed} onChange={(e) => setEv({ ...ev, passed: e.target.value })}><option value="true">Pass</option><option value="false">Fail</option></select></label>}
          {ev.kind === 'Use' && <label>Case<select value={ev.bookingId} onChange={(e) => setEv({ ...ev, bookingId: e.target.value })}><option value="">Choose today's case…</option>{cases.map((x) => <option key={x.bookingId} value={x.bookingId}>{x.patientName} – {x.procedure}</option>)}</select></label>}
          <label>Notes<input value={ev.notes} onChange={(e) => setEv({ ...ev, notes: e.target.value })} placeholder="e.g. AER cycle number" /></label>
          <div><button className="btn btn-primary" onClick={record}>Record</button></div>
        </div>}
        <table className="results compact"><thead><tr><th>When</th><th>What</th><th>Result</th><th>Patient</th><th>By</th></tr></thead>
          <tbody>{history.map((h) => <tr key={h.id}><td>{nzDateTime(h.at)}</td><td>{h.kind}{h.notes && <div className="small muted">{h.notes}</div>}</td><td>{h.passed == null ? '' : h.passed ? 'Pass' : 'FAIL'}</td><td>{h.patient ?? ''}</td><td>{h.by}</td></tr>)}</tbody></table>
      </div>}
    </div>
  );
}

function Loans() {
  const { can } = useSession();
  const { act, view } = useAct();
  const [rows, setRows] = useState<any[] | null>(null);
  const [suppliers, setSuppliers] = useState<any[]>([]);
  const [cycles, setCycles] = useState<any[]>([]);
  const [form, setForm] = useState<any>(null);
  const [step, setStep] = useState<any>(null);
  const load = () => api<any[]>('/api/assets/loans').then(setRows).catch(() => setRows([]));
  useEffect(() => { load(); api<any[]>('/api/assets/suppliers').then(setSuppliers).catch(() => undefined); api<any[]>('/api/assets/cycles').then((c) => setCycles(c.filter((x) => x.release === 'Released' && x.kind !== 'Wash' && x.kind !== 'Aer'))).catch(() => undefined); }, []);
  const NEXT: Record<string, string[]> = { Expected: ['Received'], Received: ['Checked'], Checked: ['Reprocessed'], Reprocessed: ['Used', 'Returned'], Used: ['Reprocessed', 'Returned'] };
  const save = async () => { if (await act(() => api('/api/assets/loans', { body: { id: null, supplierId: form.supplierId || null, description: form.description, itemCount: Number(form.itemCount || 0), bookingId: null } }), (r) => `Loan set ${r.number} recorded.`)) { setForm(null); load(); } };
  const advance = async () => { if (await act(() => api(`/api/assets/loans/${step.loan.id}/advance`, { body: { to: step.to, notes: step.notes || null, cycleId: step.cycleId || null } }), 'Updated.')) { setStep(null); load(); } };
  const sup = (id: string) => suppliers.find((s) => s.id === id)?.name ?? '';
  return (
    <div className="stack">
      {view}
      {can(Perm.CssdProcess) && !form && <div><button className="btn btn-light" onClick={() => setForm({ supplierId: '', description: '', itemCount: '' })}>Record a loan set</button></div>}
      {form && <div className="card subtle stack"><div className="form-grid">
        <label>Supplier<select value={form.supplierId} onChange={(e) => setForm({ ...form, supplierId: e.target.value })}><option value="">Choose…</option>{suppliers.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}</select></label>
        <label>Set (supplier's name)<input value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} /></label>
        <label>Number of items<input type="number" value={form.itemCount} onChange={(e) => setForm({ ...form, itemCount: e.target.value })} /></label></div>
        <div className="row"><button className="btn btn-primary" onClick={save}>Save</button><button className="btn btn-light" onClick={() => setForm(null)}>Cancel</button></div></div>}
      {!rows ? <p>Loading…</p> : rows.length === 0 ? <p className="muted">No loan sets in progress.</p> : (
        <table className="results"><thead><tr><th>Number</th><th>Set</th><th>Supplier</th><th>Status</th><th></th></tr></thead>
          <tbody>{rows.map((l) => <tr key={l.id}><td>{l.number}</td><td>{l.description}<div className="small muted">{l.itemCount} items{l.checkNotes ? ` · ${l.checkNotes}` : ''}</div></td><td>{sup(l.supplierId)}</td><td>{l.status}</td>
            <td className="actions">{can(Perm.CssdProcess) && (NEXT[l.status] ?? []).map((to) => <button key={to} className="btn btn-link" onClick={() => setStep({ loan: l, to, notes: '', cycleId: '' })}>{to}</button>)}</td></tr>)}</tbody></table>)}
      {step && <div className="card subtle stack"><h2>{step.loan.number} → {step.to}</h2><div className="form-grid">
        {step.to === 'Reprocessed' && <label>Steriliser load<select value={step.cycleId} onChange={(e) => setStep({ ...step, cycleId: e.target.value })}><option value="">Choose…</option>{cycles.map((c) => <option key={c.id} value={c.id}>{c.loadNumber} {nzDateTime(c.startedAt)}</option>)}</select></label>}
        <label>Notes{step.to === 'Checked' && ' (items counted, condition)'}<input value={step.notes} onChange={(e) => setStep({ ...step, notes: e.target.value })} /></label></div>
        <div className="row"><button className="btn btn-primary" onClick={advance}>Save</button><button className="btn btn-light" onClick={() => setStep(null)}>Cancel</button></div></div>}
    </div>
  );
}

function Nonconformance() {
  const { can } = useSession();
  const { act, view } = useAct();
  const [rows, setRows] = useState<any[] | null>(null);
  const [all, setAll] = useState(false);
  const [edit, setEdit] = useState<any>(null);
  const load = () => api<any[]>(`/api/assets/nonconformances${all ? '?all=true' : ''}`).then(setRows).catch(() => setRows([]));
  useEffect(() => { load(); }, [all]); // eslint-disable-line react-hooks/exhaustive-deps
  const f = (k: string, label: string, area = true) => <label>{label}{area ? <textarea rows={2} value={edit[k] ?? ''} onChange={(e) => setEdit({ ...edit, [k]: e.target.value })} /> : <input value={edit[k] ?? ''} onChange={(e) => setEdit({ ...edit, [k]: e.target.value })} />}</label>;
  const save = async () => { if (await act(() => api('/api/assets/nonconformances', { body: { ...edit, id: edit.id ?? null } }), (r) => `Saved ${r.number}.`)) { setEdit(null); load(); } };
  const close = async (n: any) => { if (await act(() => api(`/api/assets/nonconformances/${n.id}/close`, { body: {} }), `${n.number} closed.`)) load(); };
  return (
    <div className="stack">
      {view}
      <div className="row"><label className="check"><input type="checkbox" checked={all} onChange={(e) => setAll(e.target.checked)} /> Include closed</label>
        {can(Perm.CssdProcess) && !edit && <button className="btn btn-light" onClick={() => setEdit({ category: 'Process', description: '' })}>Raise a nonconformance</button>}</div>
      {edit && <div className="card subtle stack"><div className="form-grid">
        <label>Category<select value={edit.category} onChange={(e) => setEdit({ ...edit, category: e.target.value })}>{['Process', 'Failed cycle', 'Recall', 'Instrument damage', 'Missing item', 'Packaging', 'Equipment', 'Endoscope leak test failed', 'Endoscope microbiology failed', 'Supplier', 'Other'].map((c) => <option key={c}>{c}</option>)}</select></label></div>
        {f('description', 'What went wrong')}{f('immediateAction', 'Immediate action taken')}{f('rootCause', 'Root cause')}{f('correctiveAction', 'Corrective action')}{f('preventiveAction', 'Preventive action')}
        <div className="row"><button className="btn btn-primary" onClick={save}>Save</button><button className="btn btn-light" onClick={() => setEdit(null)}>Cancel</button></div></div>}
      {!rows ? <p>Loading…</p> : rows.length === 0 ? <p className="muted">No open nonconformances.</p> : (
        <table className="results"><thead><tr><th>Number</th><th>What</th><th>Status</th><th></th></tr></thead>
          <tbody>{rows.map((n) => <tr key={n.id}><td>{n.number}<div className="small muted">{formatDate(n.raisedOn)} · {n.raisedBy}</div></td><td>{n.category}<div className="small">{n.description}</div>
            {n.correctiveAction && <div className="small muted">Action: {n.correctiveAction}</div>}</td><td>{n.status === 'ActionsAgreed' ? 'Actions agreed' : n.status}</td>
            <td className="actions">{n.status !== 'Closed' && can(Perm.CssdProcess) && <button className="btn btn-link" onClick={() => setEdit(n)}>Update</button>}
              {n.status !== 'Closed' && can(Perm.CssdRelease) && <button className="btn btn-link" onClick={() => close(n)}>Close</button>}</td></tr>)}</tbody></table>)}
    </div>
  );
}

function Trace() {
  const { can } = useSession();
  const { act, view } = useAct();
  const [cycles, setCycles] = useState<any[]>([]);
  const [load, setLoad] = useState('');
  const [barcode, setBarcode] = useState('');
  const [result, setResult] = useState<any>(null);
  const [reason, setReason] = useState('');
  useEffect(() => { api<any[]>('/api/assets/cycles').then((c) => setCycles(c.filter((x) => x.kind !== 'Wash'))).catch(() => undefined); }, []);
  const find = async () => { const r = await act(() => api(`/api/assets/trace?${load ? `cycleId=${load}` : `barcode=${encodeURIComponent(barcode)}`}`)); if (r) setResult(r); };
  const recall = async () => {
    const r = await act(() => api(`/api/assets/cycles/${load}/recall`, { body: { reason } }), (x) => `Recall recorded: ${x.quarantined} tray(s) in store quarantined, nonconformance ${x.nonconformance}. ${x.cases.length} case(s) to follow up below.`);
    if (r) setResult({ trays: r.cases, scopes: [] });
  };
  return (
    <div className="stack">
      <p className="muted">Find every case a load or tray was used on (recall), or open a patient's record to see the trays, scopes and implants used on them.</p>
      <div className="form-grid">
        <label>Steriliser load<select value={load} onChange={(e) => { setLoad(e.target.value); setBarcode(''); }}><option value="">Choose…</option>{cycles.map((c) => <option key={c.id} value={c.id}>{c.loadNumber} {nzDateTime(c.startedAt)} {c.machine}</option>)}</select></label>
        <label>or tray barcode<input value={barcode} onChange={(e) => { setBarcode(e.target.value); setLoad(''); }} /></label>
        <div><button className="btn btn-primary" onClick={find} disabled={!load && !barcode}>Find cases</button></div>
      </div>
      {view}
      {result && (result.trays.length === 0 ? <p className="muted">Not issued to any case.</p> : (
        <table className="results"><thead><tr><th>Tray</th><th>Load</th><th>Patient</th><th>Case</th><th>Issued</th></tr></thead>
          <tbody>{result.trays.map((t: any) => <tr key={t.id}><td>{t.barcode}<div className="small muted">{t.tray}</div></td><td>{t.loadNumber}</td><td>{t.patientId ? <a href={`/patients/${t.patientId}`}>{t.patient}</a> : '—'}</td>
            <td>{t.caseDate ? formatDate(t.caseDate) : ''} {t.procedure}</td><td>{nzDateTime(t.issuedAt)} · {t.issuedBy}</td></tr>)}</tbody></table>))}
      {load && can(Perm.CssdRelease) && <div className="card subtle row"><label>Recall this load – why?<input value={reason} onChange={(e) => setReason(e.target.value)} placeholder="e.g. biological indicator positive" /></label>
        <button className="btn btn-warn" onClick={recall}>Recall load</button></div>}
    </div>
  );
}

/** The folder the steriliser and washer write their cycle records to – imported automatically every minute. */
function LogFolder() {
  const [v, setV] = useState<string | null>(null);
  const [msg, setMsg] = useState<string | null>(null);
  useEffect(() => { api('/api/assets/cssd-settings').then((x) => setV(x.logFolder ?? '')).catch(() => setV(null)); }, []);
  if (v === null) return null;
  return (
    <details className="card subtle"><summary>Automatic import from the machines' log folder</summary>
      <div className="row"><label>Folder (blank = off)<input value={v} onChange={(e) => setV(e.target.value)} placeholder="e.g. \\\\server\\cssd-logs" /></label>
        <button className="btn btn-light" onClick={async () => { try { await api('/api/assets/cssd-settings', { method: 'PUT', body: { logFolder: v || null } }); setMsg('Saved – records saved there are imported every minute and moved to processed\\ or failed\\.'); } catch (e) { setMsg(errText(e)); } }}>Save</button></div>
      {msg && <p className="small" role="status">{msg}</p>}
    </details>
  );
}

/** Steriliser curve: chamber temperature (dark) and pressure (blue, right scale), set temperature and the plateau checks. SVG attributes only (CSP). */
function SteriliserCurve({ r }: { r: any }) {
  const pts = r.curve as { s: number; t: number; p: number }[];
  if (!pts || pts.length < 2) return null;
  const W = 720, H = 240, L = 44, R = 50, T = 10, B = 26;
  const maxS = pts[pts.length - 1].s || 1, maxT = 140, maxP = Math.max(350, ...pts.map((x) => x.p));
  const x = (s: number) => L + ((W - L - R) * s) / maxS;
  const yT = (t: number) => T + (H - T - B) * (1 - t / maxT);
  const yP = (p: number) => T + (H - T - B) * (1 - p / maxP);
  const line = (f: (q: any) => number) => pts.map((q) => `${x(q.s).toFixed(1)},${f(q).toFixed(1)}`).join(' ');
  const mins = Math.ceil(maxS / 60);
  return (
    <div className="stack-sm">
      <h3>Cycle curve {r.cycleNumber} – {r.program}</h3>
      <svg viewBox={`0 0 ${W} ${H}`} className="spc" role="img" aria-label={`Steriliser cycle ${r.cycleNumber}: temperature and pressure over ${mins} minutes`}>
        <line x1={L} y1={T} x2={L} y2={H - B} stroke="#9AA9B4" /><line x1={L} y1={H - B} x2={W - R} y2={H - B} stroke="#9AA9B4" /><line x1={W - R} y1={T} x2={W - R} y2={H - B} stroke="#9AA9B4" />
        {[0, 35, 70, 105, 140].map((t) => <text key={t} x={L - 6} y={yT(t) + 4} textAnchor="end" fontSize="10" fill="#1B2730">{t}°</text>)}
        {[0, 100, 200, 300].filter((p) => p <= maxP).map((p) => <text key={p} x={W - R + 6} y={yP(p) + 4} fontSize="10" fill="#24688A">{p} kPa</text>)}
        {Array.from({ length: Math.floor(mins / 10) + 1 }, (_, i) => i * 10).map((m) => <text key={m} x={x(m * 60)} y={H - 8} textAnchor="middle" fontSize="10" fill="#5B6B77">{m} min</text>)}
        {r.setTemperatureC != null && <line x1={L} x2={W - R} y1={yT(r.setTemperatureC)} y2={yT(r.setTemperatureC)} stroke="#2E7D32" strokeDasharray="4 3" />}
        <polyline points={line((q) => yP(q.p))} fill="none" stroke="#24688A" strokeWidth="1.5" />
        <polyline points={line((q) => yT(q.t))} fill="none" stroke="#1B2730" strokeWidth="2" />
      </svg>
      <p className="small">Dark line: chamber temperature (left scale); blue: chamber pressure (right scale); green dashes: set temperature {r.setTemperatureC} °C. Steriliser result: {r.resultText}</p>
      <ul className="small">{r.checks.map((c: string) => <li key={c} className={/TOO|NOT|OUT|CHECK/.test(c) ? 'error-text' : ''}>{c}</li>)}</ul>
      {!r.checksOk && <p className="warn-box">BrightPAS's plateau checks did not all pass – do not release without investigating.</p>}
    </div>
  );
}

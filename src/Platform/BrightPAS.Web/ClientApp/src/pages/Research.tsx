import { useCallback, useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { addDays, api, ApiError, errText, formatDate, nzDateTime, Perm, todayIso } from '../api';
import { useSession } from '../App';
import ExportButton from '../components/ExportButton';

const errs = (e: unknown) => (e instanceof ApiError && e.body?.errors ? e.body.errors as string[] : [errText(e)]);
const LEVEL: Record<string, string> = { Pseudonymised: 'Pseudonymised (study IDs; key kept by the Privacy Officer)', DeIdentified: 'De-identified (no IDs, month/year, age bands, regions)', Aggregate: 'Aggregate only (counts, small cells suppressed)' };
const PURPOSE: Record<string, string> = { QualityImprovement: 'Quality improvement', Board: 'Board report', Benchmarking: 'External benchmarking', Teaching: 'Teaching / presentation', Research: 'Approved research', Audit: 'Clinical audit' };
const KIND: Record<string, string> = { DirectId: 'never released', BirthDate: 'age / age band', Date: 'date / month', Place: 'region', Clinician: 'coded', FreeText: 'removed unless pseudonymised', Ethnicity: 'needs Māori data governance note', Category: '', Number: '' };
const STATUS: Record<string, string> = { Submitted: 'Waiting for the Privacy Officer', Approved: 'Approved – ready to download', Declined: 'Declined', Released: 'Released', Draft: 'Draft' };

/** De-identified and anonymised reporting (spec 7.21). */
export default function Research() {
  const { can } = useSession();
  const navigate = useNavigate();
  const [list, setList] = useState<any[] | null>(null);
  const [sets, setSets] = useState<any[]>([]);
  const [f, setF] = useState<any>({ title: '', dataset: '', level: 'Aggregate', fromDate: addDays(todayIso(), -365), toDate: todayIso(), purpose: 'QualityImprovement', recipient: '', ethicsReference: '', maoriDataGovernance: '', smallCell: 5, removeSmallGroups: false, columns: [] as string[] });
  const [errors, setErrors] = useState<string[]>([]);
  useEffect(() => { api<any[]>('/api/research/extracts').then(setList).catch((e) => setErrors(errs(e))); api<any[]>('/api/research/datasets').then((d) => { setSets(d); if (d[0]) setF((x: any) => ({ ...x, dataset: d[0].key })); }).catch(() => undefined); }, []);
  const ds = sets.find((d) => d.key === f.dataset);
  const toggle = (k: string) => setF({ ...f, columns: f.columns.includes(k) ? f.columns.filter((c: string) => c !== k) : [...f.columns, k] });
  const create = async () => { setErrors([]); try { const r = await api<any>('/api/research/extracts', { body: { ...f, ethicsReference: f.ethicsReference || null, maoriDataGovernance: f.maoriDataGovernance || null } }); navigate(`/research/${r.id}`); } catch (e) { setErrors(errs(e)); } };
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>De-identified reports</h1><p className="muted">Extracts for the Board, benchmarking, quality improvement, teaching and approved research – without exposing who the patients are. Patient-level extracts need the Privacy Officer's approval.</p></div>
      {errors.length > 0 && <div className="error" role="alert"><ul>{errors.map((e) => <li key={e}>{e}</li>)}</ul></div>}
      <section className="card">
        <h2>Extracts</h2>
        {!list ? <p>Loading…</p> : list.length === 0 ? <p className="muted">None yet.</p> : (
          <table className="results"><thead><tr><th>Extract</th><th>Level</th><th>For</th><th>Status</th><th>Requested</th></tr></thead>
            <tbody>{list.map((x) => <tr key={x.id}><td><Link to={`/research/${x.id}`}><strong>{x.number}</strong></Link> {x.title}</td><td>{x.level}</td><td>{PURPOSE[x.purpose]} – {x.recipient}</td>
              <td><span className={`pill ${x.status === 'Submitted' ? 'amber' : x.status === 'Declined' ? 'red' : 'green'}`}>{STATUS[x.status]}</span></td><td className="small">{x.requestedBy}, {nzDateTime(x.requestedAt)}</td></tr>)}</tbody></table>
        )}
      </section>
      {can(Perm.ResearchBuild) && ds && (
        <section className="card stack">
          <h2>New extract</h2>
          <div className="form-grid">
            <label>Title<input value={f.title} onChange={(e) => setF({ ...f, title: e.target.value })} /></label>
            <label>Data<select value={f.dataset} onChange={(e) => setF({ ...f, dataset: e.target.value, columns: [] })}>{sets.map((d) => <option key={d.key} value={d.key}>{d.label}</option>)}</select></label>
            <label>Level<select value={f.level} onChange={(e) => setF({ ...f, level: e.target.value })}>{Object.entries(LEVEL).map(([k, v]) => <option key={k} value={k}>{v}</option>)}</select></label>
            <label>From<input type="date" value={f.fromDate} onChange={(e) => setF({ ...f, fromDate: e.target.value })} /></label>
            <label>To<input type="date" value={f.toDate} onChange={(e) => setF({ ...f, toDate: e.target.value })} /></label>
            <label>Purpose<select value={f.purpose} onChange={(e) => setF({ ...f, purpose: e.target.value })}>{Object.entries(PURPOSE).map(([k, v]) => <option key={k} value={k}>{v}</option>)}</select></label>
            <label>Who will receive it<input value={f.recipient} onChange={(e) => setF({ ...f, recipient: e.target.value })} /></label>
            {f.purpose === 'Research' && <label>Ethics approval reference (HDEC or institutional)<input value={f.ethicsReference} onChange={(e) => setF({ ...f, ethicsReference: e.target.value })} /></label>}
            <label>Hide counts under<input type="number" value={f.smallCell} onChange={(e) => setF({ ...f, smallCell: Number(e.target.value) })} /></label>
          </div>
          {f.level === 'DeIdentified' && <label className="check"><input type="checkbox" checked={f.removeSmallGroups} onChange={(e) => setF({ ...f, removeSmallGroups: e.target.checked })} /> Remove rows in groups too small to stay anonymous</label>}
          <fieldset><legend>Columns</legend><div className="col-picks">{ds.columns.map((c: any) => (
            <label key={c.key} className={`check${c.kind === 'DirectId' ? ' muted' : ''}`}><input type="checkbox" disabled={c.kind === 'DirectId'} checked={f.columns.includes(c.key)} onChange={() => toggle(c.key)} /> {c.label}{KIND[c.kind] && <span className="small muted"> ({KIND[c.kind]})</span>}</label>))}</div></fieldset>
          {f.columns.some((c: string) => ds.columns.find((d: any) => d.key === c)?.kind === 'Ethnicity') && (
            <label>How releasing ethnicity meets TDS's Māori data governance arrangements (Te Mana Raraunga)<textarea value={f.maoriDataGovernance} onChange={(e) => setF({ ...f, maoriDataGovernance: e.target.value })} /></label>)}
          <div><button className="btn btn-primary" onClick={() => void create()}>{f.level === 'Aggregate' ? 'Create' : 'Send for approval'}</button></div>
        </section>
      )}
    </div>
  );
}

export function ResearchExtract() {
  const { id } = useParams();
  const { can } = useSession();
  const [x, setX] = useState<any>(null);
  const [p, setP] = useState<any>(null);
  const [note, setNote] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState<string | null>(null);
  const load = useCallback(() => api<any>(`/api/research/extracts/${id}`).then(setX).catch((e) => setErrors(errs(e))), [id]);
  useEffect(() => { void load(); }, [load]);
  if (!x) return <p>{errors.length ? errors.join(' ') : 'Loading…'}</p>;
  const run = async (f: () => Promise<unknown>, ok: string) => { setErrors([]); setMsg(null); try { await f(); setMsg(ok); await load(); } catch (e) { setErrors(errs(e)); } };
  return (
    <div className="stack-lg">
      <div className="page-head row-between"><div><h1>{x.number} – {x.title}</h1><p className="muted">{LEVEL[x.level]} · {formatDate(x.fromDate)} – {formatDate(x.toDate)} · {PURPOSE[x.purpose]} for {x.recipient}{x.ethicsReference ? ` · ethics ${x.ethicsReference}` : ''}</p></div>
        <Link className="btn btn-light" to="/research">All extracts</Link></div>
      {errors.length > 0 && <div className="error" role="alert"><ul>{errors.map((e) => <li key={e}>{e}</li>)}</ul></div>}
      {msg && <p className="notice" role="status">{msg}</p>}
      <section className="card stack">
        <p><span className={`pill ${x.status === 'Submitted' ? 'amber' : x.status === 'Declined' ? 'red' : 'green'}`}>{STATUS[x.status]}</span> Requested by {x.requestedBy} {nzDateTime(x.requestedAt)}
          {x.decidedBy && <> · {x.status === 'Declined' ? 'declined' : 'approved'} by {x.decidedBy}{x.decidedAt ? ` ${nzDateTime(x.decidedAt)}` : ''}{x.decisionNote ? `: ${x.decisionNote}` : ''}</>}{x.downloads > 0 && <> · downloaded {x.downloads} time(s)</>}</p>
        {x.maoriDataGovernance && <p className="small"><strong>Māori data governance:</strong> {x.maoriDataGovernance}</p>}
        <div className="row wrap">
          <button className="btn btn-light" onClick={() => void run(async () => setP(await api(`/api/research/extracts/${id}/preview`, { body: {} })), 'Preview below – nothing has been released.')}>Check and preview</button>
          {(x.status === 'Approved' || x.status === 'Released') && can(Perm.ResearchBuild) && <ExportButton label="Download the extract" path={`/api/research/extracts/${id}/download`} body={{}} />}
          {x.level === 'Pseudonymised' && x.status === 'Released' && can(Perm.ResearchApprove) && <ExportButton label="Linking key (Privacy Officer)" path={`/api/research/extracts/${id}/key`} body={{}} />}
        </div>
      </section>
      {p && (
        <section className="card stack">
          <div className="stat-row">
            <div><span className="muted small">Rows</span><strong>{x.rows ?? '—'}</strong></div>
            {x.level !== 'Aggregate' && <div><span className="muted small">Smallest group</span><strong>{p.minGroupSize}</strong><span className="small">people sharing age band, sex, procedure, region, month</span></div>}
            {x.level !== 'Aggregate' && <div><span className="muted small">At risk</span><strong>{p.atRiskRows}</strong><span className="small">rows in groups under {x.smallCell}</span></div>}
            {x.level === 'Aggregate' && <div><span className="muted small">Suppressed cells</span><strong>{p.suppressedCells}</strong></div>}
          </div>
          {p.notes.length > 0 && <ul>{p.notes.map((n: string) => <li key={n}>{n}</li>)}</ul>}
          <div className="table-scroll" tabIndex={0} role="region" aria-label="Preview"><table className="results compact"><thead><tr>{p.headers.map((h: string) => <th key={h}>{h}</th>)}</tr></thead>
            <tbody>{p.rows.map((r: (string | null)[], i: number) => <tr key={i}>{r.map((v, j) => <td key={j}>{v ?? ''}</td>)}</tr>)}</tbody></table></div>
          <p className="small muted">First 25 rows shown.</p>
        </section>
      )}
      {x.status === 'Submitted' && can(Perm.ResearchApprove) && (
        <section className="card stack">
          <h2>Privacy Officer decision</h2>
          <label>Note (needed to decline, or to approve when rows are at risk)<textarea value={note} onChange={(e) => setNote(e.target.value)} /></label>
          <div className="row"><button className="btn btn-primary" onClick={() => void run(() => api(`/api/research/extracts/${id}/decide`, { body: { approve: true, note: note || null } }), 'Approved.')}>Approve</button>
            <button className="btn btn-warn" onClick={() => void run(() => api(`/api/research/extracts/${id}/decide`, { body: { approve: false, note: note || null } }), 'Declined.')}>Decline</button></div>
        </section>
      )}
    </div>
  );
}

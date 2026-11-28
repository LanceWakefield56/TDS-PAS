import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import { api, ApiError, errText, fileToBase64, formatDate, nzDateTime, Perm, todayIso } from '../api';
import { useSession } from '../App';

/** E-consent and patient education (0.29.0, ADR 0042). */
const errs = (e: unknown) => (e instanceof ApiError && e.body?.errors ? e.body.errors as string[] : [errText(e)]);
const FREQ: [string, string][] = [['Common', 'Common (more than 1 in 100)'], ['Uncommon', 'Uncommon (1 in 100 to 1 in 1,000)'], ['Rare', 'Rare (1 in 1,000 to 1 in 10,000)'], ['VeryRare', 'Very rare (less than 1 in 10,000)']];
const SIGNERS: [string, string][] = [['Patient', 'The patient'], ['Parent', 'Parent or guardian'], ['WelfareGuardian', 'Welfare guardian'], ['Epoa', 'Enduring power of attorney (EPOA)'], ['Other', 'Other person with authority']];
const STATUS_PILL: Record<string, string> = { Draft: 'pill', Sent: 'pill amber', Signed: 'pill blue', Confirmed: 'pill green', Withdrawn: 'pill red' };
const STATUS_TEXT: Record<string, string> = { Draft: 'Prepared', Sent: 'Sent to the patient', Signed: 'Signed', Confirmed: 'Confirmed on the day', Withdrawn: 'Withdrawn' };

function Errors({ list }: { list: string[] }) {
  return list.length > 0 ? <div className="error" role="alert"><ul>{list.map((e) => <li key={e}>{e}</li>)}</ul></div> : null;
}

export default function Consent() {
  const [params, setParams] = useSearchParams();
  const { can } = useSession();
  const tab = params.get('tab') ?? (params.get('booking') ? 'prepare' : 'prepare');
  const tabs: [string, string][] = [['prepare', 'To prepare'], ['sent', 'Waiting for the patient'], ['questions', 'Questions'], ['signed', 'Signed – confirm on the day'], ['confirmed', 'Confirmed'], ['withdrawn', 'Withdrawn'], ['sheets', 'Information sheets']];
  if (can(Perm.ConsentSheets) || can(Perm.ConsentApprove)) tabs.push(['settings', 'Settings']);
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Consent</h1><p className="muted">Prepare each patient's consent from an approved information sheet, send it to them by a secure link, answer their questions, and confirm it with them on the day. Paper consent forms can be scanned in.</p></div>
      <div className="tabs" role="tablist">{tabs.map(([k, t]) => <button key={k} role="tab" aria-selected={tab === k} className={tab === k ? 'on' : ''} onClick={() => setParams({ tab: k })}>{t}</button>)}</div>
      {tab === 'prepare' && <Prepare bookingId={params.get('booking')} />}
      {['sent', 'questions', 'signed', 'confirmed', 'withdrawn'].includes(tab) && <ConsentList view={tab} />}
      {tab === 'sheets' && <Sheets />}
      {tab === 'settings' && <Settings />}
    </div>
  );
}

function Prepare({ bookingId }: { bookingId: string | null }) {
  const { can } = useSession();
  const navigate = useNavigate();
  const [rows, setRows] = useState<any[] | null>(null);
  const [sheets, setSheets] = useState<any[]>([]);
  const [open, setOpen] = useState<string | null>(bookingId);
  const [errors, setErrors] = useState<string[]>([]);
  useEffect(() => {
    api<any[]>('/api/consent/prepare').then(setRows).catch((e) => setErrors(errs(e)));
    api<any[]>('/api/consent/sheets').then((s) => setSheets(s.filter((x) => x.status === 'Published'))).catch(() => {});
  }, []);
  const row = rows?.find((r) => r.bookingId === open);
  return (
    <div className="stack">
      <section className="card stack">
        <h2>Bookings without a consent</h2>
        <Errors list={errors} />
        {!rows ? <p>Loading…</p> : rows.length === 0 ? <p className="muted">Every booking in the next few weeks has a consent.</p> : (
          <div className="table-scroll" tabIndex={0} role="region" aria-label="Bookings without a consent">
            <table className="results">
              <thead><tr><th>Date</th><th>Patient</th><th>Procedure</th><th>Surgeon</th><th>Suggested sheet</th><th>Action</th></tr></thead>
              <tbody>{rows.map((r) => (
                <tr key={r.bookingId}>
                  <td>{formatDate(r.surgeryDate)}</td>
                  <td><Link to={`/patients/${r.patientId}`}>{r.patient}</Link> <span className="small muted">{r.nhi ?? ''}</span></td>
                  <td>{r.procedure}</td><td>{r.clinician ?? '—'}</td>
                  <td>{r.suggestedSheet ?? <span className="muted">None – choose one</span>}</td>
                  <td>{can(Perm.ConsentManage) && <button className="btn btn-light btn-small" onClick={() => setOpen(r.bookingId)}>Prepare</button>}</td>
                </tr>))}</tbody>
            </table>
          </div>)}
      </section>
      {open && can(Perm.ConsentManage) && <PrepareForm key={open} bookingId={open} row={row} sheets={sheets} onDone={(id) => navigate(`/consent/${id}`)} onClose={() => setOpen(null)} />}
    </div>
  );
}

function PrepareForm({ bookingId, row, sheets, onDone, onClose }: { bookingId: string; row: any; sheets: any[]; onDone: (id: string) => void; onClose: () => void }) {
  const [sheetId, setSheetId] = useState<string>(row?.suggestedSheetId ?? '');
  const [extra, setExtra] = useState('');
  const [note, setNote] = useState('');
  const [paper, setPaper] = useState(false);
  const [p, setP] = useState({ signer: 'Patient', signerName: '', relationship: '', interpreter: '', signedOn: todayIso() });
  const [file, setFile] = useState<File | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const save = async () => {
    setErrors([]); setBusy(true);
    try {
      if (paper) {
        if (!file) { setErrors(['Choose the scanned consent form (PDF, JPEG, PNG or TIFF).']); return; }
        const r = await api<any>('/api/consent/paper', { body: { bookingId, sheetId: sheetId || null, signer: p.signer, signerName: p.signerName, relationship: p.relationship || null, interpreter: p.interpreter || null, signedOn: p.signedOn, fileName: file.name, contentBase64: await fileToBase64(file) } });
        onDone(r.id);
      } else {
        const r = await api<any>('/api/consent', { body: { bookingId, sheetId, extraRisks: extra || null, noteToPatient: note || null } });
        onDone(r.id);
      }
    } catch (e) { setErrors(errs(e)); } finally { setBusy(false); }
  };
  return (
    <section className="card stack">
      <h2>Prepare the consent{row ? ` – ${row.patient}, ${row.procedure}` : ''}</h2>
      <div className="form-grid">
        <label className="span-all">Information sheet<select value={sheetId} onChange={(e) => setSheetId(e.target.value)}><option value="">Choose…</option>{sheets.map((s) => <option key={s.id} value={s.id}>{s.name} (v{s.version})</option>)}</select></label>
      </div>
      <label className="check"><input type="checkbox" checked={paper} onChange={(e) => setPaper(e.target.checked)} /> The patient has already signed a paper consent form – scan it in</label>
      {!paper ? (
        <div className="form-grid">
          <label className="span-all">Risks that apply to this patient in particular (one per line, optional)<textarea rows={3} value={extra} onChange={(e) => setExtra(e.target.value)} /></label>
          <label className="span-all">Note to the patient (optional)<textarea rows={2} value={note} onChange={(e) => setNote(e.target.value)} /></label>
        </div>
      ) : (
        <div className="form-grid">
          <label>Who signed<select value={p.signer} onChange={(e) => setP({ ...p, signer: e.target.value })}>{SIGNERS.map(([k, t]) => <option key={k} value={k}>{t}</option>)}</select></label>
          <label>Name of the person who signed<input value={p.signerName} onChange={(e) => setP({ ...p, signerName: e.target.value })} /></label>
          {p.signer !== 'Patient' && <label>Relationship or authority<input value={p.relationship} onChange={(e) => setP({ ...p, relationship: e.target.value })} /></label>}
          <label>Interpreter (if one was used)<input value={p.interpreter} onChange={(e) => setP({ ...p, interpreter: e.target.value })} /></label>
          <label>Date signed<input type="date" value={p.signedOn} onChange={(e) => setP({ ...p, signedOn: e.target.value })} /></label>
          <label className="span-all">Scanned form<input type="file" accept=".pdf,.jpg,.jpeg,.png,.tif,.tiff" onChange={(e) => setFile(e.target.files?.[0] ?? null)} /></label>
        </div>
      )}
      <Errors list={errors} />
      <div className="row"><button className="btn btn-primary" disabled={busy} onClick={() => void save()}>{paper ? 'Save the paper consent' : 'Prepare'}</button><button className="btn btn-light" onClick={onClose}>Close</button></div>
    </section>
  );
}

function ConsentList({ view }: { view: string }) {
  const [rows, setRows] = useState<any[] | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  useEffect(() => { setRows(null); api<any[]>(`/api/consent?view=${view}`).then(setRows).catch((e) => setErrors(errs(e))); }, [view]);
  return (
    <section className="card stack">
      <Errors list={errors} />
      {!rows ? <p>Loading…</p> : rows.length === 0 ? <p className="muted">Nothing here.</p> : <ConsentTable rows={rows} label="Consents" />}
    </section>
  );
}

export function ConsentTable({ rows, label }: { rows: any[]; label: string }) {
  return (
    <div className="table-scroll" tabIndex={0} role="region" aria-label={label}>
      <table className="results">
        <thead><tr><th>Consent</th><th>Date</th><th>Patient</th><th>Procedure</th><th>Status</th><th>Points to check</th></tr></thead>
        <tbody>{rows.map((r) => (
          <tr key={r.id}>
            <td><Link to={`/consent/${r.id}`}>{r.number}</Link></td>
            <td>{r.surgeryDate ? formatDate(r.surgeryDate) : '—'}</td>
            <td>{r.patient} <span className="small muted">{r.nhi ?? ''}</span></td>
            <td>{r.procedure}<div className="small muted">{r.sheet}</div></td>
            <td><span className={STATUS_PILL[r.status]}>{STATUS_TEXT[r.status]}</span>{r.method === 'Paper' && <span className="small muted"> (paper)</span>}{r.openQuestions > 0 && <div><span className="pill amber">{r.openQuestions} question{r.openQuestions > 1 ? 's' : ''}</span></div>}</td>
            <td className="small">{r.flags.join(' ')}</td>
          </tr>))}</tbody>
      </table>
    </div>
  );
}

export function ConsentDetail() {
  const { id } = useParams();
  const { can } = useSession();
  const [c, setC] = useState<any>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState<string | null>(null);
  const [link, setLink] = useState<string | null>(null);
  const [answers, setAnswers] = useState<Record<string, string>>({});
  const [note, setNote] = useState('');
  const [reason, setReason] = useState('');
  const [draft, setDraft] = useState<{ extraRisks: string; noteToPatient: string } | null>(null);
  const load = useCallback(() => api(`/api/consent/${id}`).then((x: any) => { setC(x); setDraft({ extraRisks: x.extraRisks ?? '', noteToPatient: x.noteToPatient ?? '' }); }).catch((e) => setErrors(errs(e))), [id]);
  useEffect(() => { void load(); }, [load]);
  const act = async (fn: () => Promise<unknown>, done: string) => { setErrors([]); setMsg(null); try { await fn(); setMsg(done); await load(); } catch (e) { setErrors(errs(e)); } };
  if (!c) return <section className="card"><Errors list={errors} />{errors.length === 0 && <p>Loading…</p>}</section>;
  const live = c.status === 'Draft' || c.status === 'Sent';
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Consent {c.number} <span className={STATUS_PILL[c.status]}>{STATUS_TEXT[c.status]}</span></h1>
        <p><Link to={`/patients/${c.patientId}`}>{c.patient}</Link> {c.nhi && <span className="muted">{c.nhi}</span>} · {c.procedure}{c.side ? ` (${c.side})` : ''} · {c.clinicianName ?? 'Surgeon not set'}{c.surgeryDate ? ` · ${formatDate(c.surgeryDate)}` : ''}</p></div>
      {msg && <p className="notice" role="status">{msg}</p>}
      <Errors list={errors} />
      {c.flags.length > 0 && <div className="warn-box"><strong>Points to check</strong><ul>{c.flags.map((f: string) => <li key={f}>{f}</li>)}</ul></div>}
      <section className="card stack">
        <h2>Where it is</h2>
        <dl className="facts">
          <dt>Information sheet</dt><dd>{c.sheet.name} (version {c.sheet.version}){c.sheet.approvedBy ? `, approved by ${c.sheet.approvedBy}` : ''}</dd>
          <dt>Prepared</dt><dd>{nzDateTime(c.createdAt)} by {c.createdBy}</dd>
          {c.linkSentAt && <><dt>Link sent</dt><dd>{nzDateTime(c.linkSentAt)} (works until {nzDateTime(c.linkExpiresAt)}){c.locked ? ' – locked after wrong dates of birth' : ''}</dd></>}
          {c.openedAt && <><dt>Opened by the patient</dt><dd>{nzDateTime(c.openedAt)} – read {c.sectionsRead.length} of {c.sections.length} sections</dd></>}
          {c.signedAt && <><dt>Signed</dt><dd>{nzDateTime(c.signedAt)} {c.method === 'Paper' ? 'on paper' : 'online'} by {c.signerName}{c.signer !== 'Patient' ? ` (${c.signerRelationship})` : ' (the patient)'}{c.interpreter ? ` · interpreter ${c.interpreter}` : ''}</dd></>}
          {c.confirmedAt && <><dt>Confirmed on the day</dt><dd>{nzDateTime(c.confirmedAt)} by {c.confirmedBy}{c.confirmNote ? ` – ${c.confirmNote}` : ''}</dd></>}
          {c.withdrawnAt && <><dt>Withdrawn</dt><dd>{nzDateTime(c.withdrawnAt)} by {c.withdrawnBy === 'patient-link' ? 'the patient' : c.withdrawnBy}: {c.withdrawnReason}</dd></>}
        </dl>
        <div className="row">
          <a className="btn btn-light" href={`/api/consent/${c.id}/pdf`} target="_blank" rel="noreferrer">Open the PDF</a>
          {live && can(Perm.ConsentManage) && <button className="btn btn-primary" onClick={() => void act(async () => { const r = await api<any>(`/api/consent/${c.id}/send`, { body: {} }); setLink(r.link); if (!r.delivered) setErrors(['The patient has no mobile or email that can be used – copy the link below and give it to them another way.']); }, c.status === 'Sent' ? 'A new link was sent; the old link no longer works.' : 'The link was sent to the patient.')}>{c.status === 'Sent' ? 'Send a new link' : 'Send to the patient'}</button>}
        </div>
        {link && <p className="small">Link (works once the patient enters their date of birth): <code className="break">{link}</code></p>}
      </section>
      {c.status === 'Draft' && can(Perm.ConsentManage) && draft && (
        <section className="card stack">
          <h2>Before you send it</h2>
          <label>Risks that apply to this patient in particular (one per line)<textarea rows={3} value={draft.extraRisks} onChange={(e) => setDraft({ ...draft, extraRisks: e.target.value })} /></label>
          <label>Note to the patient<textarea rows={2} value={draft.noteToPatient} onChange={(e) => setDraft({ ...draft, noteToPatient: e.target.value })} /></label>
          <div><button className="btn btn-light" onClick={() => void act(() => api(`/api/consent/${c.id}`, { method: 'PUT', body: draft }), 'Saved.')}>Save</button></div>
        </section>
      )}
      {c.questions.length > 0 && (
        <section className="card stack">
          <h2>Questions from the patient</h2>
          {c.questions.map((q: any) => (
            <div key={q.id} className="stack-sm">
              <p><strong>{nzDateTime(q.askedAt)}:</strong> {q.text}</p>
              {q.answer ? <p className="muted">Answered by {q.answeredBy}, {nzDateTime(q.answeredAt)}: {q.answer}</p> : can(Perm.ConsentManage) && (
                <div className="stack-sm">
                  <label>Answer (the patient sees this when they open their link)<textarea rows={2} value={answers[q.id] ?? ''} onChange={(e) => setAnswers({ ...answers, [q.id]: e.target.value })} /></label>
                  <div><button className="btn btn-primary btn-small" onClick={() => void act(() => api(`/api/consent/questions/${q.id}/answer`, { body: { text: answers[q.id] ?? '' } }), 'Answer saved.')}>Save the answer</button></div>
                </div>)}
            </div>))}
        </section>
      )}
      {c.status === 'Signed' && can(Perm.ConsentConfirm) && (
        <section className="card stack">
          <h2>Confirm with the patient on the day</h2>
          <p>Go through the consent with the patient: the procedure and side, the main risks, and any questions. Confirming ticks the theatre consent check and files the confirmed copy in the patient record.</p>
          <label>Note (optional)<input value={note} onChange={(e) => setNote(e.target.value)} /></label>
          <div><button className="btn btn-primary" onClick={() => void act(() => api(`/api/consent/${c.id}/confirm`, { body: { text: note || null } }), 'Consent confirmed.')}>Confirm consent</button></div>
        </section>
      )}
      <section className="card stack">
        <h2>What the patient reads</h2>
        <SheetText sheet={c.sheet} extraRisks={c.extraRisks} />
      </section>
      {c.status !== 'Withdrawn' && can(Perm.ConsentManage) && (
        <section className="card stack">
          <h2>Withdraw</h2>
          <p className="small muted">If the patient withdraws consent, or the booking changes, withdraw this consent and prepare a new one.</p>
          <label>Reason<input value={reason} onChange={(e) => setReason(e.target.value)} /></label>
          <div><button className="btn btn-warn" onClick={() => void act(() => api(`/api/consent/${c.id}/withdraw`, { body: { reason } }), 'Consent withdrawn.')}>Withdraw consent</button></div>
        </section>
      )}
    </div>
  );
}

function SheetText({ sheet, extraRisks }: { sheet: any; extraRisks?: string | null }) {
  const para = (t: string | null | undefined) => (t ?? '').split('\n').filter(Boolean).map((l, i) => <p key={i}>{l}</p>);
  const extras = (extraRisks ?? '').split('\n').map((x) => x.trim()).filter(Boolean);
  return (
    <div className="stack-sm consent-text">
      <h3>What the procedure is</h3>{para(sheet.about)}
      <h3>Benefits</h3>{para(sheet.benefits)}
      <h3>Risks</h3>
      <ul>{sheet.risks.map((r: any, i: number) => <li key={i}>{r.serious && <strong>Serious: </strong>}{r.text} – <span className="muted">{r.frequency.replace('VeryRare', 'Very rare')}</span></li>)}
        {extras.map((x) => <li key={x}><strong>Applies to this patient: </strong>{x}</li>)}</ul>
      <h3>Alternatives</h3>{para(sheet.alternatives)}
      <h3>If the patient does not have it</h3>{para(sheet.noTreatment)}
      {sheet.anaesthetic && <><h3>Anaesthetic</h3>{para(sheet.anaesthetic)}</>}
      {sheet.preparation && <><h3>Getting ready</h3>{para(sheet.preparation)}</>}
      {sheet.recovery && <><h3>Recovery</h3>{para(sheet.recovery)}</>}
      {sheet.links?.length > 0 && <><h3>Videos and web pages</h3><ul>{sheet.links.map((l: any) => <li key={l.url}><a href={l.url} target="_blank" rel="noreferrer">{l.title}</a></li>)}</ul></>}
    </div>
  );
}

function Sheets() {
  const { can } = useSession();
  const navigate = useNavigate();
  const [rows, setRows] = useState<any[] | null>(null);
  const [retired, setRetired] = useState(false);
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState<string | null>(null);
  const load = useCallback(() => api<any[]>(`/api/consent/sheets?retired=${retired}`).then(setRows).catch((e) => setErrors(errs(e))), [retired]);
  useEffect(() => { void load(); }, [load]);
  return (
    <section className="card stack">
      <h2>Procedure information sheets</h2>
      <p className="small muted">Each sheet is written as a draft and approved by a second clinician before patients see it. Approved sheets do not change: make a new version instead. Consents keep the version the patient read.</p>
      <div className="row">
        {can(Perm.ConsentSheets) && <button className="btn btn-primary" onClick={() => navigate('/consent/sheets/new')}>New sheet</button>}
        {can(Perm.ConsentSheets) && <button className="btn btn-light" onClick={async () => { setErrors([]); try { const r = await api<any>('/api/consent/sheets/examples', { body: {} }); setMsg(`${r.added} example sheet${r.added === 1 ? '' : 's'} added as drafts.`); await load(); } catch (e) { setErrors(errs(e)); } }}>Add example sheets</button>}
        <label className="check"><input type="checkbox" checked={retired} onChange={(e) => setRetired(e.target.checked)} /> Show retired</label>
      </div>
      {msg && <p className="notice" role="status">{msg}</p>}
      <Errors list={errors} />
      {!rows ? <p>Loading…</p> : (
        <div className="table-scroll" tabIndex={0} role="region" aria-label="Information sheets">
          <table className="results">
            <thead><tr><th>Sheet</th><th>Code</th><th>Version</th><th>Status</th><th>Used for</th><th>Approved</th></tr></thead>
            <tbody>{rows.map((s) => (
              <tr key={s.id}>
                <td><Link to={`/consent/sheets/${s.id}`}>{s.name}</Link></td><td>{s.code}</td><td>{s.version}</td>
                <td><span className={s.status === 'Published' ? 'pill green' : s.status === 'Draft' ? 'pill amber' : 'pill'}>{s.status === 'Published' ? 'In use' : s.status}</span></td>
                <td className="small">{s.procedureWords ?? '—'}</td>
                <td className="small">{s.approvedBy ? `${s.approvedBy}, ${nzDateTime(s.approvedAt)}` : '—'}</td>
              </tr>))}</tbody>
          </table>
        </div>)}
    </section>
  );
}

const EMPTY = { code: '', name: '', procedureWords: '', about: '', benefits: '', alternatives: '', noTreatment: '', anaesthetic: '', preparation: '', recovery: '', risks: [{ text: '', frequency: 'Common', serious: false }], links: [] as { title: string; url: string }[] };

export function SheetEditor() {
  const { id } = useParams();
  const { can } = useSession();
  const navigate = useNavigate();
  const isNew = id === 'new';
  const [s, setS] = useState<any>(isNew ? { ...EMPTY, status: 'Draft' } : null);
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState<string | null>(null);
  const [note, setNote] = useState('');
  const load = useCallback(() => { if (!isNew) api(`/api/consent/sheets/${id}`).then(setS).catch((e) => setErrors(errs(e))); }, [id, isNew]);
  useEffect(() => { load(); }, [load]);
  const act = async (fn: () => Promise<any>, done: string, go?: (r: any) => string) => { setErrors([]); setMsg(null); try { const r = await fn(); setMsg(done); if (go) navigate(go(r)); else load(); } catch (e) { setErrors(errs(e)); } };
  if (!s) return <section className="card"><Errors list={errors} />{errors.length === 0 && <p>Loading…</p>}</section>;
  const edit = s.status === 'Draft' && can(Perm.ConsentSheets);
  const body = () => ({ ...s, risks: s.risks.filter((r: any) => r.text.trim()), links: s.links.filter((l: any) => l.title.trim() || l.url.trim()) });
  const field = (k: string, label: string, rows = 3) => <label className="span-all">{label}<textarea rows={rows} value={s[k] ?? ''} disabled={!edit} onChange={(e) => setS({ ...s, [k]: e.target.value })} /></label>;
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>{isNew ? 'New information sheet' : `${s.name} – version ${s.version}`}</h1>
        <p><Link to="/consent?tab=sheets">All sheets</Link>{!isNew && <> · <span className={s.status === 'Published' ? 'pill green' : s.status === 'Draft' ? 'pill amber' : 'pill'}>{s.status === 'Published' ? 'In use' : s.status}</span></>}{s.approvedBy && ` · approved by ${s.approvedBy} ${nzDateTime(s.approvedAt)}`}</p></div>
      {msg && <p className="notice" role="status">{msg}</p>}
      <Errors list={errors} />
      <section className="card stack">
        <div className="form-grid">
          <label>Code<input value={s.code} disabled={!edit} onChange={(e) => setS({ ...s, code: e.target.value })} placeholder="LAPCHOLE" /></label>
          <label>Name<input value={s.name} disabled={!edit} onChange={(e) => setS({ ...s, name: e.target.value })} /></label>
          <label className="span-all">Words in the booked procedure that pick this sheet (comma separated)<input value={s.procedureWords ?? ''} disabled={!edit} onChange={(e) => setS({ ...s, procedureWords: e.target.value })} placeholder="cholecystectomy, gallbladder" /></label>
          {field('about', 'What the procedure is')}
          {field('benefits', 'Benefits')}
          {field('alternatives', 'Alternatives (including not having it)')}
          {field('noTreatment', 'What may happen without the procedure')}
          {field('anaesthetic', 'Anaesthetic (optional)', 2)}
          {field('preparation', 'Getting ready (optional)', 2)}
          {field('recovery', 'After the procedure (optional)', 2)}
        </div>
      </section>
      <section className="card stack">
        <h2>Risks</h2>
        <p className="small muted">Serious risks are always shown first, even when rare.</p>
        {s.risks.map((r: any, i: number) => (
          <div key={i} className="form-grid">
            <label className="span-all">Risk<input value={r.text} disabled={!edit} onChange={(e) => setS({ ...s, risks: s.risks.map((x: any, j: number) => j === i ? { ...x, text: e.target.value } : x) })} /></label>
            <label>How often<select value={r.frequency} disabled={!edit} onChange={(e) => setS({ ...s, risks: s.risks.map((x: any, j: number) => j === i ? { ...x, frequency: e.target.value } : x) })}>{FREQ.map(([k, t]) => <option key={k} value={k}>{t}</option>)}</select></label>
            <label className="check"><input type="checkbox" checked={r.serious} disabled={!edit} onChange={(e) => setS({ ...s, risks: s.risks.map((x: any, j: number) => j === i ? { ...x, serious: e.target.checked } : x) })} /> Serious</label>
          </div>))}
        {edit && <div><button className="btn btn-light btn-small" onClick={() => setS({ ...s, risks: [...s.risks, { text: '', frequency: 'Common', serious: false }] })}>Add a risk</button></div>}
        <h2>Videos and web pages</h2>
        {s.links.map((l: any, i: number) => (
          <div key={i} className="form-grid">
            <label>Title<input value={l.title} disabled={!edit} onChange={(e) => setS({ ...s, links: s.links.map((x: any, j: number) => j === i ? { ...x, title: e.target.value } : x) })} /></label>
            <label className="span-all">Address (https://…)<input value={l.url} disabled={!edit} onChange={(e) => setS({ ...s, links: s.links.map((x: any, j: number) => j === i ? { ...x, url: e.target.value } : x) })} /></label>
          </div>))}
        {edit && <div><button className="btn btn-light btn-small" onClick={() => setS({ ...s, links: [...s.links, { title: '', url: '' }] })}>Add a video or web page</button></div>}
      </section>
      <section className="card stack">
        <div className="row">
          {edit && <button className="btn btn-primary" onClick={() => void act(() => isNew ? api('/api/consent/sheets', { body: body() }) : api(`/api/consent/sheets/${id}`, { method: 'PUT', body: body() }), 'Draft saved.', isNew ? (r) => `/consent/sheets/${r.id}` : undefined)}>Save draft</button>}
          {!isNew && s.status !== 'Draft' && can(Perm.ConsentSheets) && <button className="btn btn-light" onClick={() => void act(() => api(`/api/consent/sheets/${id}/new-version`, { body: {} }), 'New draft version made.', (r) => `/consent/sheets/${r.id}`)}>Make a new version</button>}
          {!isNew && s.status !== 'Retired' && (can(Perm.ConsentSheets) || can(Perm.ConsentApprove)) && <button className="btn btn-light" onClick={() => void act(() => api(`/api/consent/sheets/${id}/retire`, { body: {} }), s.status === 'Draft' ? 'Draft removed.' : 'Sheet retired.', () => '/consent?tab=sheets')}>{s.status === 'Draft' ? 'Delete draft' : 'Retire'}</button>}
        </div>
        {!isNew && s.status === 'Draft' && can(Perm.ConsentApprove) && (
          <div className="stack-sm">
            <p className="small">Approving publishes this version for patients and retires the previous one. A clinician other than the last editor ({s.updatedBy}) must approve.</p>
            <label>Approval note (optional)<input value={note} onChange={(e) => setNote(e.target.value)} /></label>
            <div><button className="btn btn-primary" onClick={() => void act(() => api(`/api/consent/sheets/${id}/approve`, { body: { note: note || null } }), 'Approved and in use.')}>Approve and publish</button></div>
          </div>)}
      </section>
    </div>
  );
}

function Settings() {
  const [s, setS] = useState<any>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState<string | null>(null);
  const load = () => api('/api/consent/settings').then(setS).catch((e) => setErrors(errs(e)));
  useEffect(() => { void load(); }, []);
  if (!s) return <section className="card"><p>{errors.join(' ') || 'Loading…'}</p></section>;
  const save = async () => {
    setErrors([]); setMsg(null);
    try { await api('/api/consent/settings', { method: 'PUT', body: { publicUrl: s.publicUrl || null, linkDays: Number(s.linkDays), requireBeforeConfirm: s.requireBeforeConfirm, prepareDaysAhead: Number(s.prepareDaysAhead) } }); setMsg('Settings saved.'); await load(); }
    catch (e) { setErrors(errs(e)); }
  };
  return (
    <section className="card stack">
      <h2>Settings</h2>
      <div className="form-grid">
        <label className="span-all">Public web address for patient links<input value={s.publicUrl ?? ''} onChange={(e) => setS({ ...s, publicUrl: e.target.value })} placeholder="https://portal.example.co.nz" /></label>
        <label>Links work for (days)<input value={s.linkDays} inputMode="numeric" onChange={(e) => setS({ ...s, linkDays: e.target.value })} /></label>
        <label>Show bookings this many days ahead on "To prepare"<input value={s.prepareDaysAhead} inputMode="numeric" onChange={(e) => setS({ ...s, prepareDaysAhead: e.target.value })} /></label>
      </div>
      <label className="check"><input type="checkbox" checked={s.requireBeforeConfirm} onChange={(e) => setS({ ...s, requireBeforeConfirm: e.target.checked })} /> A theatre booking cannot be confirmed until the consent is signed</label>
      {msg && <p className="notice" role="status">{msg}</p>}
      <Errors list={errors} />
      <div><button className="btn btn-primary" onClick={() => void save()}>Save settings</button></div>
    </section>
  );
}

// ------------------------------------------------------------------ the patient's page (no sign-in)

function SignaturePad({ onChange }: { onChange: (png: string | null) => void }) {
  const ref = useRef<HTMLCanvasElement>(null);
  const drawing = useRef(false);
  const used = useRef(false);
  const pos = (e: React.PointerEvent<HTMLCanvasElement>) => { const c = ref.current!; const r = c.getBoundingClientRect(); return [(e.clientX - r.left) * (c.width / r.width), (e.clientY - r.top) * (c.height / r.height)]; };
  const down = (e: React.PointerEvent<HTMLCanvasElement>) => { const ctx = ref.current!.getContext('2d')!; drawing.current = true; ctx.lineWidth = 2.5; ctx.lineCap = 'round'; ctx.strokeStyle = '#102a43'; const [x, y] = pos(e); ctx.beginPath(); ctx.moveTo(x, y); ref.current!.setPointerCapture(e.pointerId); };
  const move = (e: React.PointerEvent<HTMLCanvasElement>) => { if (!drawing.current) return; const ctx = ref.current!.getContext('2d')!; const [x, y] = pos(e); ctx.lineTo(x, y); ctx.stroke(); used.current = true; };
  const up = () => { if (!drawing.current) return; drawing.current = false; onChange(used.current ? ref.current!.toDataURL('image/png') : null); };
  const clear = () => { const c = ref.current!; c.getContext('2d')!.clearRect(0, 0, c.width, c.height); used.current = false; onChange(null); };
  return (
    <div className="stack-sm">
      <canvas ref={ref} width={600} height={160} className="signature-pad" aria-label="Draw your signature (optional)" onPointerDown={down} onPointerMove={move} onPointerUp={up} onPointerLeave={up} />
      <div><button type="button" className="btn btn-light btn-small" onClick={clear}>Clear the signature</button></div>
    </div>
  );
}

export function PatientConsent() {
  const { token } = useParams();
  const [stage, setStage] = useState<'loading' | 'dob' | 'read' | 'done' | 'closed' | 'signed' | 'withdrawn'>('loading');
  const [dob, setDob] = useState('');
  const [v, setV] = useState<any>(null);
  const [read, setRead] = useState<string[]>([]);
  const [question, setQuestion] = useState('');
  const [sent, setSent] = useState(false);
  const [sign, setSign] = useState({ signer: 'Patient', signerName: '', relationship: '', interpreter: '', understood: false, hadChanceToAsk: false, agree: false });
  const [sig, setSig] = useState<string | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const [reason, setReason] = useState('');
  useEffect(() => { api<{ status: string }>(`/api/e/${token}`).then((r) => setStage(r.status === 'open' || r.status === 'signed' ? 'dob' : 'closed')).catch(() => setStage('closed')); }, [token]);
  const fail = (e: unknown) => { setErrors(errs(e)); window.scrollTo(0, 0); };
  const open = async () => { const r = await api<any>(`/api/e/${token}/open`, { body: { dateOfBirth: dob } }); setV(r); setRead(r.read); setStage(r.status === 'Signed' ? 'signed' : 'read'); };
  const allRead = v && v.sections.every((s: any) => read.includes(s.key));
  const waiting = v?.questions.some((q: any) => !q.answer);
  return (
    <div className="q-page">
      <header className="q-head"><strong>Tasman Day Surgery</strong> · Your procedure and consent</header>
      <main className="q-body stack-lg">
        {stage === 'loading' && <p>Loading…</p>}
        {stage === 'closed' && <section className="card"><h1>This link is no longer active</h1><p>Please phone Tasman Day Surgery if you have any questions about your procedure.</p></section>}
        {stage === 'withdrawn' && <section className="card"><h1>Your consent has been withdrawn</h1><p>We have let your surgeon's team know. They will be in touch. If you change your mind, please phone us.</p></section>}
        {stage === 'done' && <section className="card"><h1>Thank you</h1><p>Your consent has been signed and sent to Tasman Day Surgery. On the day, a member of the team will go through it with you again – you can still ask questions or change your mind at any time.</p></section>}
        {stage === 'dob' && (
          <form className="card stack" onSubmit={async (e) => { e.preventDefault(); setErrors([]); try { await open(); } catch (x) { fail(x); } }}>
            <h1>Kia ora</h1><p>To keep your information private, enter your date of birth.</p>
            <label>Date of birth<input type="date" value={dob} onChange={(e) => setDob(e.target.value)} required /></label>
            <Errors list={errors} />
            <div><button className="btn btn-primary btn-big">Open</button></div>
          </form>
        )}
        {stage === 'signed' && v && (
          <section className="card stack">
            <h1>You have signed your consent</h1>
            <p>{v.procedure}{v.side ? ` (${v.side})` : ''}{v.surgeryDate ? ` on ${formatDate(v.surgeryDate)}` : ''}. If you no longer want to go ahead, you can withdraw your consent here, or phone us.</p>
            <label>Reason (optional)<input value={reason} onChange={(e) => setReason(e.target.value)} /></label>
            <Errors list={errors} />
            <div><button className="btn btn-warn" onClick={async () => { setErrors([]); try { await api(`/api/e/${token}/withdraw`, { body: { dateOfBirth: dob, reason: reason || null } }); setStage('withdrawn'); } catch (x) { fail(x); } }}>Withdraw my consent</button></div>
          </section>
        )}
        {stage === 'read' && v && (
          <>
            <section className="card stack">
              <h1>Kia ora {v.givenName}</h1>
              <p>Your procedure: <strong>{v.procedure}{v.side ? ` – ${v.side}` : ''}</strong>{v.clinician ? ` with ${v.clinician}` : ''}{v.surgeryDate ? ` on ${formatDate(v.surgeryDate)}` : ''}.</p>
              <p>Please read each section below and tick "I have read this". You can ask your surgeon's team a question at any time, and you don't have to sign until you are happy.</p>
              {v.noteToPatient && <p className="notice">{v.noteToPatient}</p>}
              <p className="small muted">Read {read.length} of {v.sections.length} sections.</p>
            </section>
            <Errors list={errors} />
            {v.sections.map((s: any) => (
              <section key={s.key} className="card stack-sm consent-text">
                <h2>{s.title}</h2>
                {s.text && s.text.split('\n').filter(Boolean).map((l: string, i: number) => <p key={i}>{l}</p>)}
                {s.risks && <ul>{s.risks.map((r: any, i: number) => <li key={i}>{r.serious && <strong>Serious: </strong>}{r.text} – <span className="muted">{r.frequency}</span></li>)}</ul>}
                <label className="check"><input type="checkbox" checked={read.includes(s.key)} disabled={read.includes(s.key)} onChange={async () => { setErrors([]); const before = read; setRead([...read, s.key]); try { const r = await api<any>(`/api/e/${token}/read`, { body: { dateOfBirth: dob, section: s.key } }); setRead(r.read); } catch (x) { setRead(before); fail(x); } }} /> I have read this</label>
              </section>))}
            {v.links.length > 0 && <section className="card stack-sm"><h2>Videos and more information</h2><ul>{v.links.map((l: any) => <li key={l.url}><a href={l.url} target="_blank" rel="noreferrer">{l.title}</a></li>)}</ul></section>}
            <section className="card stack">
              <h2>Questions</h2>
              {v.questions.map((q: any, i: number) => <div key={i}><p><strong>You asked:</strong> {q.question}</p><p>{q.answer ? <><strong>Answer:</strong> {q.answer}</> : <span className="muted">Waiting for an answer – we will reply here, or phone you.</span>}</p></div>)}
              <label>Ask your surgeon's team a question<textarea rows={3} value={question} onChange={(e) => setQuestion(e.target.value)} /></label>
              {sent && <p className="notice" role="status">Your question has been sent. Open this link again later to see the answer.</p>}
              <div><button className="btn btn-light" onClick={async () => { setErrors([]); try { await api(`/api/e/${token}/ask`, { body: { dateOfBirth: dob, text: question } }); setQuestion(''); setSent(true); await open(); } catch (x) { fail(x); } }}>Send my question</button></div>
            </section>
            <section className="card stack">
              <h2>Sign</h2>
              {!allRead && <p className="muted">Please read every section above first.</p>}
              {waiting && <p className="muted">You can sign once we have answered your question.</p>}
              <div className="form-grid">
                <label>Who is signing<select value={sign.signer} onChange={(e) => setSign({ ...sign, signer: e.target.value })}>{SIGNERS.map(([k, t]) => <option key={k} value={k}>{k === 'Patient' ? 'I am the patient' : t}</option>)}</select></label>
                <label>Type your full name<input value={sign.signerName} onChange={(e) => setSign({ ...sign, signerName: e.target.value })} autoComplete="name" /></label>
                {sign.signer !== 'Patient' && <label>Your relationship to the patient, or your authority<input value={sign.relationship} onChange={(e) => setSign({ ...sign, relationship: e.target.value })} /></label>}
                <label>Interpreter's name and language (if someone interpreted for you)<input value={sign.interpreter} onChange={(e) => setSign({ ...sign, interpreter: e.target.value })} /></label>
              </div>
              <p className="small">Draw your signature (optional):</p>
              <SignaturePad onChange={setSig} />
              <label className="check"><input type="checkbox" checked={sign.understood} onChange={(e) => setSign({ ...sign, understood: e.target.checked })} /> I have read the information about the procedure, including the risks, benefits and alternatives, and I understand it.</label>
              <label className="check"><input type="checkbox" checked={sign.hadChanceToAsk} onChange={(e) => setSign({ ...sign, hadChanceToAsk: e.target.checked })} /> I have had the chance to ask questions and I am happy with the answers.</label>
              <label className="check"><input type="checkbox" checked={sign.agree} onChange={(e) => setSign({ ...sign, agree: e.target.checked })} /> I agree to have the procedure described above. I know I can change my mind at any time before it.</label>
              <div><button className="btn btn-primary btn-big" disabled={!allRead || waiting} onClick={async () => { setErrors([]); try { await api(`/api/e/${token}/sign`, { body: { dateOfBirth: dob, ...sign, relationship: sign.relationship || null, interpreter: sign.interpreter || null, signature: sig } }); setStage('done'); window.scrollTo(0, 0); } catch (x) { fail(x); } }}>Sign my consent</button></div>
            </section>
          </>
        )}
      </main>
    </div>
  );
}

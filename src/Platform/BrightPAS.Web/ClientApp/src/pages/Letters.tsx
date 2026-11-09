import { useEffect, useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import { api, errText, formatDate, nzDateTime, PatientSummary, Perm } from '../api';
import { useSession } from '../App';
import PatientPicker from '../components/PatientPicker';
import ExportButton from '../components/ExportButton';

export interface LetterRow {
  id: string; number: string; patientId: string; patientName: string; nhi: string | null; mrn: string; kind: string; recipient: string; recipientName: string;
  subject: string; status: string; channel: string; createdAt: string; createdBy: string; sentAt: string | null; ackCode: string | null; canHealthLink: boolean;
}
interface Letter {
  id: string; number: string; patientId: string; kind: string; recipient: string; recipientName: string; recipientAddress: string | null; subject: string; body: string;
  status: string; channel: string; printCount: number; messageControlId: string | null; ackCode: string | null; ackText: string | null; ackAt: string | null; voidReason: string | null;
  createdAt: string; createdBy: string; author: string | null; finalisedAt: string | null; finalisedBy: string | null; sentAt: string | null; sentBy: string | null;
  practice: { name: string; healthLinkEdi: string | null; channel: string } | null;
}
interface Org { organisationName: string; addressLine1: string | null; addressLine2: string | null; suburb: string | null; city: string | null; postcode: string | null; phone: string | null; email: string | null }
export const letterStatusClass: Record<string, string> = { Draft: 'amber', Final: 'blue', Sent: 'green', Acknowledged: 'green', Failed: 'red', Void: '' };
const recipientLabel: Record<string, string> = { Patient: 'Patient', Gp: 'GP', Referrer: 'Referrer', Other: 'Other' };

/** Letters list: drafts, letters to print or send, recent letters and delivery problems. */
export default function Letters() {
  const { can, writable } = useSession();
  const [view, setView] = useState('drafts');
  const [rows, setRows] = useState<LetterRow[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => { setRows(null); api<LetterRow[]>(`/api/letters?view=${view}`).then(setRows).catch((e) => setError(errText(e))); }, [view]);
  return (
    <div className="stack-lg">
      <div className="row-between">
        <div className="page-head"><h1>Letters</h1><p className="muted">Letters to patients, GPs and referrers. Write from a template, check, finalise, then print or send to the GP via HealthLink.</p></div>
        <div className="row">
          {can(Perm.LettersTemplates) && <Link className="btn btn-light" to="/admin/letters">Templates &amp; GP practices</Link>}
          {can(Perm.LettersWrite) && writable('letters') && <Link className="btn btn-primary" to="/letters/new">Write a letter</Link>}
        </div>
      </div>
      <section className="card">
        <div className="tabs">{[['drafts', 'Drafts'], ['to-send', 'To print or send'], ['recent', 'Last 60 days'], ['problems', 'Delivery problems']].map(([k, t]) =>
          <button key={k} className={view === k ? 'on' : ''} onClick={() => setView(k)}>{t}</button>)}</div>
        {error && <p className="error">{error}</p>}
        {rows?.length === 0 && <p className="muted">Nothing here.</p>}
        {rows && rows.length > 0 && <LetterTable rows={rows} />}
      </section>
    </div>
  );
}

export function LetterTable({ rows, showPatient = true }: { rows: LetterRow[]; showPatient?: boolean }) {
  const navigate = useNavigate();
  return (
    <table className="results">
      <thead><tr><th>Letter</th>{showPatient && <th>Patient</th>}<th>To</th><th>Subject</th><th>Status</th></tr></thead>
      <tbody>{rows.map((r) => (
        <tr key={r.id} tabIndex={0} onClick={() => navigate(`/letters/${r.id}`)} onKeyDown={(e) => e.key === 'Enter' && navigate(`/letters/${r.id}`)}>
          <td data-label="Letter"><strong>{r.number}</strong><div className="small muted">{nzDateTime(r.createdAt)} · {r.createdBy}</div></td>
          {showPatient && <td data-label="Patient">{r.patientName}<div className="small muted">{r.nhi ?? '—'} · {r.mrn}</div></td>}
          <td data-label="To">{r.recipientName}<div className="small muted">{recipientLabel[r.recipient]}</div></td>
          <td data-label="Subject" className="small">{r.subject}</td>
          <td data-label="Status"><span className={`pill ${letterStatusClass[r.status]}`}>{r.status}</span>{r.channel !== 'None' && <div className="small muted">{r.channel === 'HealthLink' ? 'HealthLink' : 'Printed'}{r.ackCode ? ` · ${r.ackCode}` : ''}</div>}</td>
        </tr>
      ))}</tbody>
    </table>
  );
}

/** New letter: choose patient, template and (when the template needs it) the booking or referral it is about. */
export function LetterNew() {
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const [patient, setPatient] = useState<PatientSummary | null>(null);
  const [templates, setTemplates] = useState<{ code: string; name: string; kind: string; recipient: string; isActive: boolean }[]>([]);
  const [code, setCode] = useState(params.get('template') ?? '');
  const [bookings, setBookings] = useState<any[] | null>(null);
  const [refs, setRefs] = useState<any[] | null>(null);
  const [bookingId, setBookingId] = useState(params.get('booking') ?? '');
  const [referralId, setReferralId] = useState(params.get('referral') ?? '');
  const [errors, setErrors] = useState<string[]>([]);
  const { mod } = useSession();
  useEffect(() => {
    api<{ templates: any[] }>('/api/letters/templates').then((r) => setTemplates(r.templates.filter((t: any) => t.isActive))).catch(() => undefined);
    const pid = params.get('patient');
    if (pid) api<any>(`/api/patients/${pid}`).then(setPatient).catch(() => undefined);
  }, []);
  useEffect(() => {
    if (!patient) return;
    if (mod('theatre')) api<any[]>(`/api/theatre/patients/${patient.id}`).then((b) => setBookings(b.filter((x) => x.status !== 'Cancelled'))).catch(() => setBookings([]));
    if (mod('referrals')) api<any[]>(`/api/referrals?patientId=${patient.id}`).then(setRefs).catch(() => setRefs([]));
  }, [patient]);
  const t = templates.find((x) => x.code === code);
  const needsBooking = t && ['Appointment', 'Admission', 'GpNotification'].includes(t.kind);
  const needsReferral = t && ['ReferralAcknowledgement', 'ReferralDeclined'].includes(t.kind);
  const create = async () => {
    setErrors([]);
    try {
      const r = await api<{ id: string }>('/api/letters', { body: { patientId: patient!.id, templateCode: code, bookingId: bookingId || null, referralId: referralId || null } });
      navigate(`/letters/${r.id}`, { replace: true });
    } catch (x: any) { setErrors(x?.body?.errors ?? [errText(x)]); }
  };
  return (
    <div className="stack-lg">
      <div className="page-head"><p className="small"><Link to="/letters">← Letters</Link></p><h1>Write a letter</h1></div>
      <section className="card stack">
        <PatientPicker value={patient} onChange={(p) => { setPatient(p); setBookingId(''); setReferralId(''); }} />
        <label>Template<select value={code} onChange={(e) => setCode(e.target.value)}><option value="">Choose…</option>
          {templates.map((x) => <option key={x.code} value={x.code}>{x.name}</option>)}</select></label>
        {patient && needsBooking && (
          <label>Booking this letter is about<select value={bookingId} onChange={(e) => setBookingId(e.target.value)}><option value="">Choose…</option>
            {bookings?.map((b) => <option key={b.id} value={b.id}>{formatDate(b.date)} · {b.procedure} · {b.surgeon} ({b.status})</option>)}</select>
            {bookings?.length === 0 && <span className="hint">No bookings for this patient.</span>}</label>
        )}
        {patient && needsReferral && (
          <label>Referral this letter is about<select value={referralId} onChange={(e) => setReferralId(e.target.value)}><option value="">Choose…</option>
            {refs?.map((r) => <option key={r.id} value={r.id}>{r.number} · {formatDate(r.receivedOn)} from {r.referrerName} ({r.status})</option>)}</select></label>
        )}
        {errors.length > 0 && <div className="error" role="alert">{errors.map((e) => <div key={e}>{e}</div>)}</div>}
        <div><button className="btn btn-primary" disabled={!patient || !code} onClick={create}>Create draft</button></div>
        <p className="hint">The draft is filled in from the template. You can change the wording before finalising it.</p>
      </section>
    </div>
  );
}

/** One letter: edit while draft, finalise, print (with letterhead), send via HealthLink, void. */
export function LetterView() {
  const { id } = useParams();
  const { can, writable } = useSession();
  const [l, setL] = useState<Letter | null>(null);
  const [org, setOrg] = useState<Org | null>(null);
  const [edit, setEdit] = useState<{ recipientName: string; recipientAddress: string; subject: string; body: string; author: string } | null>(null);
  const [voiding, setVoiding] = useState(false);
  const [reason, setReason] = useState('');
  const [msg, setMsg] = useState<string | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const canWrite = can(Perm.LettersWrite) && writable('letters');
  const load = async () => {
    const x = await api<Letter>(`/api/letters/${id}`); setL(x);
    if (x.status === 'Draft') setEdit({ recipientName: x.recipientName, recipientAddress: x.recipientAddress ?? '', subject: x.subject, body: x.body, author: x.author ?? '' });
    else setEdit(null);
  };
  useEffect(() => { load().catch((e) => setErrors([errText(e)])); api<Org>('/api/letters/letterhead').then(setOrg).catch(() => undefined); }, [id]);
  const act = async (fn: () => Promise<any>, ok?: string) => { setErrors([]); setMsg(null); try { const r = await fn(); setMsg(r?.message ?? ok ?? null); await load(); } catch (x: any) { setErrors(x?.body?.errors ?? [errText(x)]); } };
  const saveDraft = () => act(() => api(`/api/letters/${id}`, { method: 'PUT', body: edit }), 'Draft saved.');
  const finalise = () => act(async () => { await api(`/api/letters/${id}`, { method: 'PUT', body: edit }); await api(`/api/letters/${id}/finalise`, { body: {} }); }, 'Letter is final – now print it or send it.');
  const print = async () => { window.print(); await act(() => api(`/api/letters/${id}/printed`, { body: {} })); };
  if (!l) return errors.length ? <p className="error">{errors[0]}</p> : <p>Loading…</p>;
  const clinicAddress = org ? [org.addressLine1, org.addressLine2, org.suburb, [org.city, org.postcode].filter(Boolean).join(' ')].filter(Boolean).join(', ') : '';
  const canSend = canWrite && (l.status === 'Final' || l.status === 'Failed') && l.recipient !== 'Patient' && !!l.practice?.healthLinkEdi;
  return (
    <div className="stack-lg">
      <div className="row-between no-print">
        <div className="page-head"><p className="small"><Link to="/letters">← Letters</Link> · <Link to={`/patients/${l.patientId}`}>Patient record</Link></p>
          <h1>{l.number} <span className={`pill ${letterStatusClass[l.status]}`}>{l.status}</span></h1>
          <p className="muted">Created {nzDateTime(l.createdAt)} by {l.createdBy}{l.finalisedAt ? ` · final ${nzDateTime(l.finalisedAt)} by ${l.finalisedBy}` : ''}{l.sentAt ? ` · sent ${nzDateTime(l.sentAt)} (${l.channel === 'HealthLink' ? 'HealthLink' : 'printed'})` : ''}</p></div>
        <div className="row">
          {canWrite && l.status === 'Draft' && <><button className="btn btn-light" onClick={saveDraft}>Save draft</button><button className="btn btn-primary" onClick={finalise}>Finalise</button></>}
          {l.status !== 'Draft' && l.status !== 'Void' && <button className={`btn ${l.status === 'Final' && !canSend ? 'btn-primary' : 'btn-light'}`} onClick={print}>Print</button>}
          {l.status !== 'Void' && <ExportButton label="Download PDF" formats={['pdf']} path={`/api/letters/${id}/pdf`} body={{}} />}
          {canSend && <button className="btn btn-primary" onClick={() => act(() => api(`/api/letters/${id}/send`, { body: {} }))}>Send to {l.practice!.name} via HealthLink</button>}
          {canWrite && (l.status === 'Draft' || l.status === 'Final' || l.status === 'Failed') && <button className="btn btn-link danger" onClick={() => setVoiding(true)}>Void</button>}
        </div>
      </div>
      {msg && <p className="notice no-print" role="status">{msg}</p>}
      {errors.length > 0 && <div className="error no-print" role="alert">{errors.map((e) => <div key={e}>{e}</div>)}</div>}
      {l.voidReason && <p className="warn-box">Void: {l.voidReason}</p>}
      {l.channel === 'HealthLink' && <p className={`no-print ${l.status === 'Failed' ? 'error' : 'notice'}`}>HealthLink {l.messageControlId}: {l.ackText ?? 'waiting for acknowledgement'}{l.ackAt ? ` (${nzDateTime(l.ackAt)})` : ''}</p>}
      {l.recipient !== 'Patient' && !l.practice?.healthLinkEdi && l.status === 'Final' && <p className="hint no-print">This recipient has no HealthLink account recorded – print and post the letter.</p>}
      {voiding && (
        <div className="card subtle stack no-print"><label>Reason for voiding<input value={reason} onChange={(e) => setReason(e.target.value)} /></label>
          <div className="row"><button className="btn btn-warn" onClick={() => act(() => api(`/api/letters/${id}/void`, { body: { reason } }), 'Letter voided.').then(() => setVoiding(false))}>Void letter</button>
            <button className="btn btn-light" onClick={() => setVoiding(false)}>Cancel</button></div></div>
      )}
      {edit ? (
        <section className="card stack no-print">
          <div className="form-grid">
            <label>To<input value={edit.recipientName} onChange={(e) => setEdit({ ...edit, recipientName: e.target.value })} /></label>
            <label>Signed by<input value={edit.author} onChange={(e) => setEdit({ ...edit, author: e.target.value })} /></label>
          </div>
          <label>Address<textarea className="short" value={edit.recipientAddress} onChange={(e) => setEdit({ ...edit, recipientAddress: e.target.value })} /></label>
          <label>Subject<input value={edit.subject} onChange={(e) => setEdit({ ...edit, subject: e.target.value })} /></label>
          <label>Letter<textarea className="letter-edit" value={edit.body} onChange={(e) => setEdit({ ...edit, body: e.target.value })} /></label>
          <p className="hint">Anything still in {'{curly brackets}'} must be filled in or removed before the letter can be finalised.</p>
        </section>
      ) : (
        <article className="letter-sheet">
          <header className="letterhead"><strong>{org?.organisationName ?? ''}</strong><div className="small">{clinicAddress}</div><div className="small">{[org?.phone, org?.email].filter(Boolean).join(' · ')}</div></header>
          <p>{l.finalisedAt ? new Date(l.finalisedAt).toLocaleDateString('en-NZ', { day: 'numeric', month: 'long', year: 'numeric' }) : ''}</p>
          <p className="pre">{l.recipientName}{l.recipientAddress ? '\n' + l.recipientAddress : ''}</p>
          <p><strong>{l.subject}</strong></p>
          <div className="pre">{l.body}</div>
          <footer className="small muted">Ref {l.number}</footer>
        </article>
      )}
    </div>
  );
}

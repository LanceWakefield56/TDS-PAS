import { useEffect, useState } from 'react';
import { api, errText, formatDate, nzDateTime, Perm } from '../api';
import { useSession } from '../App';
import { BookingRequestForm, RequestDetail, RequestList, RequestRow } from '../components/BookingRequests';

/** Portal for specialists and their practice assistants: their own TDS theatre cases, clinic appointments and waitlist, and (0.28.0) booking requests to TDS. */
export default function SpecialistPortal() {
  const [d, setD] = useState<any>(null);
  const [tab, setTab] = useState('cases');
  const [patient, setPatient] = useState<any>(null);
  const [error, setError] = useState<string | null>(null);
  const { can, mod } = useSession();
  const bookings = mod('ebookings') && can(Perm.EBookingsSend);
  const [requests, setRequests] = useState<RequestRow[]>([]);
  const [openReq, setOpenReq] = useState<string | null>(null);
  const [sent, setSent] = useState<string | null>(null);
  const loadRequests = () => { if (bookings) api<RequestRow[]>('/api/ebookings/rooms').then(setRequests).catch(() => setRequests([])); };
  useEffect(() => { api('/api/portal/specialist').then(setD).catch((e) => setError(errText(e))); }, []);
  useEffect(loadRequests, [bookings]); // eslint-disable-line react-hooks/exhaustive-deps
  const open = async (id: string) => { setError(null); try { setPatient(await api(`/api/portal/specialist/patients/${id}`)); } catch (x) { setError(errText(x)); } };
  if (error && !d) return <div className="card"><p className="error">{error}</p></div>;
  if (!d) return <p>Loading…</p>;
  const P = ({ id, name }: { id: string; name: string }) => <button className="btn btn-link" onClick={() => open(id)}>{name}</button>;
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>My TDS patients</h1><p className="muted">{d.clinicians.length ? `For ${d.clinicians.join(', ')}.` : 'Your account is not linked to a specialist yet – ask the TDS administrator.'}</p></div>
      <div className="tabs">{[['cases', `Theatre (${d.cases.length})`], ['appointments', `Clinics (${d.appointments.length})`], ['waitlist', `Waitlist (${d.waitlist.length})`],
        ...(bookings ? [['requests', `Booking requests (${requests.filter((r) => r.status === 'Submitted' || r.status === 'Query').length} open)`], ['new', 'New booking request']] : [])].map(([k, t]) => <button key={k} className={tab === k ? 'on' : ''} onClick={() => { setTab(k); setSent(null); }}>{t}</button>)}</div>
      {bookings && requests.some((r) => r.status === 'Query') && tab !== 'requests' && <p className="warn-box">TDS has asked a question about {requests.filter((r) => r.status === 'Query').map((r) => r.number).join(', ')} – open Booking requests to answer.</p>}
      {error && <p className="error">{error}</p>}
      {tab === 'new' && <section className="card stack"><h2>New booking request</h2><BookingRequestForm base="/api/ebookings/rooms" onDone={(id, number) => { setSent(`Booking request ${number} sent to TDS.`); setOpenReq(id); setTab('requests'); loadRequests(); }} /></section>}
      {tab === 'requests' && <section className="card stack">{sent && <p className="notice" role="status">{sent}</p>}<RequestList rows={requests} office={false} label="My booking requests" onOpen={setOpenReq} /></section>}
      {tab === 'requests' && openReq && <RequestDetail id={openReq} base="/api/ebookings/rooms" office={false} onChanged={loadRequests} onClose={() => setOpenReq(null)} />}
      {['cases', 'appointments', 'waitlist'].includes(tab) && <section className="card">
        {tab === 'cases' && <table className="results"><thead><tr><th>Date</th><th>Patient</th><th>Procedure</th><th>List</th><th>Status</th></tr></thead><tbody>
          {d.cases.map((c: any) => <tr key={c.id}><td>{formatDate(c.date)}</td><td><P id={c.patientId} name={c.patient} /><div className="small muted">{c.nhi}</div></td><td>{c.procedure}</td>
            <td className="small">{c.location} {c.startTime?.slice(0, 5)} · {c.surgeon}{c.anaesthetist ? ` / ${c.anaesthetist}` : ''}</td><td>{c.status}{c.isTentative && ' (tentative)'}</td></tr>)}</tbody></table>}
        {tab === 'appointments' && <table className="results"><thead><tr><th>Date</th><th>Time</th><th>Patient</th><th>Type</th><th>Status</th></tr></thead><tbody>
          {d.appointments.map((a: any) => <tr key={a.id}><td>{formatDate(a.date)}</td><td>{a.startTime.slice(0, 5)}</td><td><P id={a.patientId} name={a.patient} /></td><td>{a.kind}</td><td>{a.status}</td></tr>)}</tbody></table>}
        {tab === 'waitlist' && <table className="results"><thead><tr><th>Patient</th><th>Procedure</th><th>Priority</th><th>Added</th><th>Target</th><th>Status</th></tr></thead><tbody>
          {d.waitlist.map((w: any) => <tr key={w.id}><td><P id={w.patientId} name={w.patient} /></td><td>{w.procedure}</td><td>{w.priority}</td><td>{formatDate(w.addedOn)}</td>
            <td>{formatDate(w.targetDate)}{w.overdue && <span className="pill bad"> overdue</span>}</td><td>{w.status}{w.bookedFor && ` ${formatDate(w.bookedFor)}`}</td></tr>)}</tbody></table>}
      </section>}
      {patient && <section className="card stack">
        <div className="row-between"><h2>{patient.familyName.toUpperCase()}, {patient.givenNames}</h2><button className="btn btn-light btn-small" onClick={() => setPatient(null)}>Close</button></div>
        <p>NHI {patient.nhi ?? '—'} · {patient.mrn} · born {formatDate(patient.dateOfBirth)} · {patient.sex} · {patient.mobilePhone ?? ''}</p>
        {patient.alerts.length > 0 && <div className="alert-critical">{patient.alerts.map((a: any) => `${a.type}: ${a.description}`).join(' · ')}</div>}
        <h3>Documents</h3>
        {patient.documents.length === 0 ? <p className="muted">None.</p> : <ul className="plain">{patient.documents.map((x: any) => <li key={x.id}><a href={`/api/portal/specialist/documents/${x.id}`} target="_blank" rel="noopener noreferrer">{x.title}</a> <span className="small muted">{x.category} · {nzDateTime(x.uploadedAt)}</span></li>)}</ul>}
        <p className="hint">Every record you open is recorded in the TDS audit log.</p>
      </section>}
    </div>
  );
}

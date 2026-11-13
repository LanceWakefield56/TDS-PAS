import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router';
import { api, errText, formatDate, nzDateTime, Perm } from '../api';
import { useSession } from '../App';
import ExportButton from '../components/ExportButton';

/** Emergency transfer pack: everything the receiving hospital needs, with an SBAR handover. Print it to go with the patient, or export it protected. */
export default function TransferPack() {
  const { id } = useParams();
  const { can } = useSession();
  const [pack, setPack] = useState<any>(null);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState<string | null>(null);
  const [t, setT] = useState({ receivingFacility: 'Nelson Hospital Emergency Department', reason: '', situation: '', background: '', assessment: '', recommendation: '', escort: '', transportMode: 'Ambulance' });
  useEffect(() => { api(`/api/patients/${id}/transfer-pack`).then(setPack).catch((e) => setError(errText(e))); }, [id]);
  if (error && !pack) return <div className="card"><p className="error">{error}</p></div>;
  if (!pack) return <p>Loading…</p>;
  const p = pack.patient;
  const body = { ...t, patientId: id, bookingId: pack.bookings[0]?.id ?? null };
  const save = async () => {
    setError(null);
    try { await api(`/api/patients/${id}/transfers`, { body }); setSaved(`Transfer recorded ${new Date().toLocaleTimeString('en-NZ')}.`); } catch (x: any) { setError(x?.body?.errors?.join(' ') ?? errText(x)); }
  };
  const field = (k: keyof typeof t, label: string, area = false) => <label key={k}>{label}{area ? <textarea rows={3} value={t[k]} onChange={(e) => setT({ ...t, [k]: e.target.value })} /> : <input value={t[k]} onChange={(e) => setT({ ...t, [k]: e.target.value })} />}</label>;
  return (
    <div className="stack-lg transfer-pack">
      <div className="page-head no-print"><h1>Emergency transfer pack</h1><p><Link to={`/patients/${id}`}>Back to the patient</Link></p></div>
      <section className="card stack no-print">
        <h2>Handover (SBAR)</h2>
        <div className="form-grid">{field('receivingFacility', 'Receiving hospital')}{field('transportMode', 'Transport')}{field('escort', 'Escort (name, role)')}{field('reason', 'Reason for transfer')}</div>
        <div className="form-grid">{field('situation', 'Situation', true)}{field('background', 'Background', true)}{field('assessment', 'Assessment (latest observations)', true)}{field('recommendation', 'Recommendation', true)}</div>
        {error && <p className="error" role="alert">{error}</p>}
        {saved && <p className="ok-text">{saved}</p>}
        <div className="row">{can(Perm.PatientEdit) && <button className="btn btn-primary" onClick={save}>Record transfer</button>}<button className="btn btn-light" onClick={() => { setError(null); window.print(); }}>Print pack</button>
          <ExportButton label="Export pack (protected)" formats={["pdf", "zip"]} path={`/api/patients/${id}/transfer-pack/export`} body={{ transfer: body }} /></div>
      </section>
      <section className="card stack print-sheet">
        <h2>{pack.from?.organisation} – emergency transfer</h2>
        <p><strong>{p.familyName.toUpperCase()}, {p.givenNames}</strong> · born {formatDate(p.dateOfBirth)} · {p.sex} · NHI {p.nhi ?? 'not recorded'} · {p.mrn}<br />{p.address} · {p.mobilePhone}</p>
        <p>To: <strong>{t.receivingFacility}</strong> · {t.transportMode} · Escort: {t.escort || '—'}<br />Reason: {t.reason || '—'}</p>
        <dl className="facts"><dt>Situation</dt><dd>{t.situation || '—'}</dd><dt>Background</dt><dd>{t.background || '—'}</dd><dt>Assessment</dt><dd>{t.assessment || '—'}</dd><dt>Recommendation</dt><dd>{t.recommendation || '—'}</dd></dl>
        <h3>Alerts and allergies</h3>
        {pack.alerts.length === 0 ? <p>No alerts recorded – confirm with the patient.</p> : <ul>{pack.alerts.map((a: any, i: number) => <li key={i} className="alert-item sev-critical"><strong>{a.type}</strong> ({a.severity}): {a.description}</li>)}</ul>}
        <h3>Procedure</h3>
        <table className="results compact"><thead><tr><th>Date</th><th>Procedure</th><th>Surgeon</th><th>Anaesthetist</th><th>Anaesthetic</th><th>In / out</th></tr></thead>
          <tbody>{pack.bookings.map((b: any) => <tr key={b.id}><td>{formatDate(b.date)}</td><td>{b.procedure} {b.laterality !== 'NotApplicable' ? b.laterality : ''}</td><td>{b.surgeon}</td><td>{b.anaesthetist ?? '—'}</td><td>{b.anaesthetic}</td><td>{b.theatreIn ?? '—'} / {b.theatreOut ?? '—'}</td></tr>)}</tbody></table>
        {pack.preAdmission && <><h3>Pre-admission questionnaire</h3><p>Triage: {pack.preAdmission.triage ?? 'not triaged'} {pack.preAdmission.triageOutcome}</p><ul>{pack.preAdmission.flags.map((f: any, i: number) => <li key={i}>{f.level}: {f.text}</li>)}</ul></>}
        <h3>Recent lab results</h3>
        {pack.labs.length === 0 ? <p className="muted">None.</p> : <ul>{pack.labs.map((l: any, i: number) => <li key={i}>{l.testName} – {nzDateTime(l.reportedAt)} {l.isAbnormal && <strong>ABNORMAL</strong>} {l.comment}</li>)}</ul>}
        <h3>GP</h3><p>{pack.gp ? `${pack.gp.name} ${pack.gp.phone ?? ''}` : 'Not recorded'}</p>
        {pack.documents.length > 0 && <><h3>Documents on file</h3><p className="small">{pack.documents.map((d: any) => d.title).join(' · ')}</p></>}
        <p className="small muted">Printed from BrightPAS {new Date().toLocaleString('en-NZ')}. Contains health information.</p>
      </section>
    </div>
  );
}

import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router';
import { api, errText, formatDate, nzDateTime, PatientSummary, Perm } from '../api';
import { useSession } from '../App';
import PatientPicker from '../components/PatientPicker';
import type { LabRow } from './LabsInbox';

interface Obs { sequence: number; code: string; name: string; value: string; units: string | null; referenceRange: string | null; flag: string | null; comment: string | null; isAbnormal: boolean }
interface Detail { row: LabRow; comment: string | null; accessionNumber: string | null; acknowledgementComment: string | null; patientDateOfBirth: string | null; patientFamilyName: string; patientGivenNames: string; observations: Obs[] }
const flagText: Record<string, string> = { H: 'High', L: 'Low', HH: 'Critically high', LL: 'Critically low', A: 'Abnormal', AA: 'Critically abnormal', N: '' };

export default function LabReport() {
  const { id } = useParams();
  const { can, writable } = useSession();
  const [d, setD] = useState<Detail | null>(null);
  const [comment, setComment] = useState('');
  const [patient, setPatient] = useState<PatientSummary | null>(null);
  const [reason, setReason] = useState('');
  const [error, setError] = useState<string | null>(null);
  const load = async () => { try { setD(await api<Detail>(`/api/labs/${id}`)); } catch (e) { setError(errText(e)); } };
  useEffect(() => { load(); }, [id]); // eslint-disable-line react-hooks/exhaustive-deps
  const act = async (fn: () => Promise<unknown>) => { setError(null); try { await fn(); await load(); } catch (e) { setError(errText(e)); } };

  if (!d) return error ? <p className="error">{error}</p> : <p>Loading…</p>;
  const r = d.row;
  return (
    <div className="stack-lg">
      <p className="no-print"><Link to="/labs">← Lab results</Link></p>
      <section className="banner">
        <h1>{r.testName}</h1>
        <div className="banner-facts">
          <span><b>Patient</b> {r.patientId ? <Link className="on-dark" to={`/patients/${r.patientId}`}>{r.patientName}</Link> : r.patientName}</span>
          <span><b>NHI</b> {r.nhi ?? '—'}</span><span><b>Lab</b> {r.laboratory}</span><span><b>Accession</b> {d.accessionNumber ?? '—'}</span>
          <span><b>Collected</b> {nzDateTime(r.collectedAt)}</span><span><b>Reported</b> {nzDateTime(r.reportedAt)}</span>
          <span><b>Requested by</b> {r.requestedBy ?? '—'}</span><span><b>Status</b> {r.resultStatus}</span>
        </div>
      </section>
      {error && <p className="error" role="alert">{error}</p>}
      <section className="card">
        <h2>Results</h2>
        <table className="results compact">
          <thead><tr><th>Test</th><th>Result</th><th>Units</th><th>Reference range</th><th>Flag</th></tr></thead>
          <tbody>{d.observations.map((o) => (
            <tr key={o.sequence} className={o.isAbnormal ? 'abnormal' : ''}>
              <td data-label="Test">{o.name}{o.comment && <div className="muted small">{o.comment}</div>}</td>
              <td data-label="Result" className={`pre flag-${o.flag ?? ''}`}>{o.value}</td>
              <td data-label="Units">{o.units ?? ''}</td><td data-label="Reference range">{o.referenceRange ?? ''}</td>
              <td data-label="Flag" className={`flag-${o.flag ?? ''}`}>{o.flag ? (flagText[o.flag] ?? o.flag) : ''}</td>
            </tr>
          ))}</tbody>
        </table>
        {d.comment && <p className="pre"><strong>Lab comment:</strong> {d.comment}</p>}
      </section>

      {r.patientId === null && (
        <section className="card alert-box">
          <h2>Not matched to a patient</h2>
          <p>{r.matchNote}</p>
          <p>The lab sent: <strong>{d.patientFamilyName.toUpperCase()}, {d.patientGivenNames}</strong> · NHI {r.nhi ?? '—'} · born {d.patientDateOfBirth ? formatDate(d.patientDateOfBirth) : '—'}</p>
          {can(Perm.LabsMatch) && writable('labs') && (
            <div className="stack">
              <PatientPicker value={patient} onChange={setPatient} label="Correct patient" />
              <label>How did you confirm this is the right patient?<input value={reason} onChange={(e) => setReason(e.target.value)} placeholder="e.g. Phoned the lab, confirmed date of birth and address" /></label>
              <div><button className="btn btn-primary" disabled={!patient || !reason.trim()} onClick={() => act(() => api(`/api/labs/${id}/match`, { body: { patientId: patient!.id, reason } }))}>Match to this patient</button></div>
            </div>
          )}
        </section>
      )}

      {r.patientId !== null && (
        <section className="card action">
          <h2>Acknowledgement</h2>
          {r.acknowledgedAt ? (
            <p>Acknowledged by <strong>{r.acknowledgedBy}</strong> on {nzDateTime(r.acknowledgedAt)}{d.acknowledgementComment && <> – “{d.acknowledgementComment}”</>}</p>
          ) : can(Perm.LabsAck) && writable('labs') ? (
            <div className="stack">
              <label>Comment or action taken (optional)<input value={comment} onChange={(e) => setComment(e.target.value)} placeholder="e.g. Reviewed – iron studies ordered" /></label>
              <div><button className="btn btn-primary btn-big" onClick={() => act(() => api(`/api/labs/${id}/acknowledge`, { body: { comment } }))}>I have reviewed this result</button></div>
            </div>
          ) : <p className="muted">Not yet acknowledged.</p>}
        </section>
      )}
      <div className="no-print"><button className="btn btn-light" onClick={() => window.print()}>Print</button></div>
    </div>
  );
}

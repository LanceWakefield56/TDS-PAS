import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router';
import { api, errText, money, PatientSummary } from '../api';
import PatientPicker from '../components/PatientPicker';
import type { Fee } from './Billing';

interface Line { code: string; description: string; quantity: number; unitPrice: number | null }

export default function BillingNew() {
  const [params] = useSearchParams();
  const kind = params.get('kind') === 'Invoice' ? 'Invoice' : 'Estimate';
  const navigate = useNavigate();
  const [patient, setPatient] = useState<PatientSummary | null>(null);
  const [fees, setFees] = useState<Fee[]>([]);
  const [settings, setSettings] = useState({ surgeonPercent: 40, anaesthetistPercent: 20, gstPercent: 15 });
  const [f, setF] = useState({ payer: 'SelfFunded', payerName: '', payerReference: '', surgeon: '', anaesthetist: '', notes: '' });
  const [lines, setLines] = useState<Line[]>([{ code: '', description: '', quantity: 1, unitPrice: null }]);
  const [errors, setErrors] = useState<string[]>([]);
  useEffect(() => {
    api<Fee[]>('/api/billing/fees').then(setFees).catch(() => undefined);
    api('/api/billing/settings').then(setSettings).catch(() => undefined);
    const pid = params.get('patient');
    if (pid) api<PatientSummary>(`/api/patients/${pid}`).then(setPatient).catch(() => undefined);
  }, [params]);

  const preview = useMemo(() => {
    let sub = 0, gst = 0, sur = 0, an = 0;
    for (const l of lines) {
      const fee = fees.find((x) => x.code === l.code);
      const price = l.unitPrice ?? fee?.price ?? 0;
      const net = Math.round(price * l.quantity * 100) / 100;
      sub += net;
      if (!fee || fee.gstApplicable) gst += Math.round(net * settings.gstPercent) / 100;
      if (fee?.split) { sur += net * (fee.surgeonPercent ?? settings.surgeonPercent) / 100; an += net * (fee.anaesthetistPercent ?? settings.anaesthetistPercent) / 100; }
    }
    return { sub, gst, total: sub + gst, sur, an, fac: sub - sur - an };
  }, [lines, fees, settings]);

  const setLine = (i: number, l: Partial<Line>) => setLines(lines.map((x, j) => (j === i ? { ...x, ...l } : x)));
  const save = async () => {
    if (!patient) return;
    setErrors([]);
    try {
      const r = await api<{ id: string }>(`/api/billing/${kind === 'Invoice' ? 'invoices' : 'estimates'}`, {
        body: { patientId: patient.id, ...f, lines: lines.filter((l) => l.code || l.description).map((l) => ({ code: l.code || null, description: l.description || null, quantity: l.quantity, unitPrice: l.unitPrice })) },
      });
      navigate(`/billing/${r.id}`);
    } catch (e: any) { setErrors(e?.body?.errors ?? [errText(e)]); }
  };

  return (
    <div className="stack-lg">
      <div className="page-head"><h1>New {kind.toLowerCase()}</h1></div>
      <section className="card stack">
        <PatientPicker value={patient} onChange={setPatient} />
        <div className="form-grid">
          <label>Who pays<select value={f.payer} onChange={(e) => setF({ ...f, payer: e.target.value })}>{[['SelfFunded', 'Patient (self-funded)'], ['Insurer', 'Health insurer'], ['Acc', 'ACC'], ['HealthNz', 'Health NZ'], ['Other', 'Other']].map(([v, t]) => <option key={v} value={v}>{t}</option>)}</select></label>
          {f.payer === 'Insurer' && <label>Insurer<input value={f.payerName} onChange={(e) => setF({ ...f, payerName: e.target.value })} placeholder="e.g. Southern Cross" /></label>}
          {f.payer !== 'SelfFunded' && <label>Policy / claim / approval number<input value={f.payerReference} onChange={(e) => setF({ ...f, payerReference: e.target.value })} /></label>}
          <label>Surgeon / endoscopist<input value={f.surgeon} onChange={(e) => setF({ ...f, surgeon: e.target.value })} /></label>
          <label>Anaesthetist<input value={f.anaesthetist} onChange={(e) => setF({ ...f, anaesthetist: e.target.value })} /></label>
        </div>
      </section>
      <section className="card stack">
        <h2>Items</h2>
        {fees.length === 0 && <p className="warn-box">The fee schedule is empty. Ask Finance to add fee items (Estimates &amp; invoices › Fee schedule).</p>}
        {lines.map((l, i) => {
          const fee = fees.find((x) => x.code === l.code);
          return (
            <div key={i} className="form-grid">
              <label>Item<select value={l.code} onChange={(e) => setLine(i, { code: e.target.value, unitPrice: null })}>
                <option value="">Other (type below)</option>{fees.map((x) => <option key={x.code} value={x.code}>{x.code} – {x.description}</option>)}</select></label>
              {!l.code && <label>Description<input value={l.description} onChange={(e) => setLine(i, { description: e.target.value })} /></label>}
              <label>Quantity<input type="number" min={1} step={1} value={l.quantity} onChange={(e) => setLine(i, { quantity: Number(e.target.value) })} /></label>
              <label>Price excl GST<input type="number" step="0.01" value={l.unitPrice ?? fee?.price ?? ''} onChange={(e) => setLine(i, { unitPrice: e.target.value === '' ? null : Number(e.target.value) })} /></label>
              <div className="row"><button type="button" className="btn btn-link danger" onClick={() => setLines(lines.filter((_, j) => j !== i))}>Remove</button></div>
            </div>
          );
        })}
        <div><button type="button" className="btn btn-light" onClick={() => setLines([...lines, { code: '', description: '', quantity: 1, unitPrice: null }])}>Add item</button></div>
        <table className="totals"><tbody>
          <tr><th scope="row">Subtotal</th><td className="money">{money(preview.sub)}</td></tr>
          <tr><th scope="row">GST</th><td className="money">{money(preview.gst)}</td></tr>
          <tr className="grand"><th scope="row">Total</th><td className="money">{money(preview.total)}</td></tr>
          <tr><th scope="row" className="muted">Surgeon share</th><td className="money muted">{money(preview.sur)}</td></tr>
          <tr><th scope="row" className="muted">Anaesthetist share</th><td className="money muted">{money(preview.an)}</td></tr>
          <tr><th scope="row" className="muted">Facility share</th><td className="money muted">{money(preview.fac)}</td></tr>
        </tbody></table>
        <label>Notes (printed)<textarea value={f.notes} onChange={(e) => setF({ ...f, notes: e.target.value })} /></label>
        {errors.length > 0 && <div className="error"><ul>{errors.map((e) => <li key={e}>{e}</li>)}</ul></div>}
        <div className="row"><button className="btn btn-light" onClick={() => navigate('/billing')}>Cancel</button><button className="btn btn-primary" disabled={!patient} onClick={save}>Create {kind.toLowerCase()}</button></div>
      </section>
    </div>
  );
}

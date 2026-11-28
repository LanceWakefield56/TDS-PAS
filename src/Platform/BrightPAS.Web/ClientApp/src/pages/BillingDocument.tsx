import { useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { api, errText, formatDate, money, PatientDetail, Perm, todayIso } from '../api';
import { useSession } from '../App';
import { docStatusClass, payerText, Totals } from './Billing';
import { InvoiceClaim, TakePayment } from './Claims';
import { InvoicePayOnline } from './Payments';

interface Doc {
  id: string; kind: string; number: string | null; patientId: string; sourceEstimateId: string | null; payer: string; payerName: string | null; payerReference: string | null;
  surgeon: string | null; anaesthetist: string | null; status: string; gstPercent: number; createdAt: string; createdBy: string; issuedOn: string | null; dueOn: string | null;
  notes: string | null; voidReason: string | null; totals: Totals;
  lines: { sequence: number; code: string; description: string; quantity: number; unitPrice: number; gstApplicable: boolean; net: number }[];
  payments: { paidOn: string; amount: number; method: string; reference: string | null; recordedBy: string }[];
}

export default function BillingDocument() {
  const { id } = useParams();
  const navigate = useNavigate();
  const { can, writable, mod } = useSession();
  const [d, setD] = useState<Doc | null>(null);
  const [p, setP] = useState<PatientDetail | null>(null);
  const [settings, setSettings] = useState<{ gstNumber: string | null; bankAccount: string | null } | null>(null);
  const [pay, setPay] = useState({ paidOn: todayIso(), amount: '', method: 'Eftpos', reference: '' });
  const [error, setError] = useState<string | null>(null);
  const load = async () => {
    try { const x = await api<Doc>(`/api/billing/documents/${id}`); setD(x); setP(await api<PatientDetail>(`/api/patients/${x.patientId}`)); } catch (e) { setError(errText(e)); }
  };
  useEffect(() => { load(); api('/api/billing/settings').then(setSettings).catch(() => undefined); }, [id]); // eslint-disable-line react-hooks/exhaustive-deps
  const act = async (fn: () => Promise<unknown>) => { setError(null); try { await fn(); await load(); } catch (e) { setError(errText(e)); } };
  const status = (s: string, needReason = false) => act(async () => {
    const reason = needReason ? window.prompt('Reason?') : null;
    if (needReason && !reason) return;
    await api(`/api/billing/documents/${id}/status`, { body: { status: s, reason } });
  });
  if (!d) return error ? <p className="error">{error}</p> : <p>Loading…</p>;
  const manage = can(Perm.BillingManage) && writable('billing');
  const isInv = d.kind === 'Invoice';
  const address = p ? [p.addressLine1, p.addressLine2, p.suburb, p.city, p.postcode].filter(Boolean).join(', ') : '';
  return (
    <div className="stack-lg">
      <p className="no-print"><Link to="/billing">← Estimates &amp; invoices</Link></p>
      {error && <p className="error" role="alert">{error}</p>}
      <section className="card stack">
        <div className="doc-head">
          <div><h1>{isInv ? (d.status === 'Draft' ? 'Draft invoice' : 'Tax invoice') : 'Estimate'} {d.number ?? ''}</h1>
            <p className="muted">Tasman Day Surgery{settings?.gstNumber ? ` · GST number ${settings.gstNumber}` : ''}</p>
            <span className={`pill ${docStatusClass(d.status)}`}>{d.status === 'PartPaid' ? 'Part paid' : d.status}</span></div>
          <dl className="facts">
            <dt>{isInv ? 'Invoice date' : 'Date'}</dt><dd>{d.issuedOn ? formatDate(d.issuedOn) : '—'}</dd>
            <dt>{isInv ? 'Due' : 'Valid until'}</dt><dd>{d.dueOn ? formatDate(d.dueOn) : '—'}</dd>
            <dt>Payer</dt><dd>{payerText(d.payer, d.payerName)}{d.payerReference ? ` (${d.payerReference})` : ''}</dd>
          </dl>
        </div>
        {p && <div><strong>{p.givenNames} {p.familyName}</strong><div className="muted">{address || 'No address recorded'}</div><div className="muted small">NHI {p.nhi ?? '—'} · {p.mrn}</div></div>}
        {(d.surgeon || d.anaesthetist) && <p className="muted">{d.surgeon && <>Surgeon: {d.surgeon}. </>}{d.anaesthetist && <>Anaesthetist: {d.anaesthetist}.</>}</p>}
        <table className="results compact">
          <thead><tr><th>Item</th><th className="money">Qty</th><th className="money">Price</th><th className="money">Amount excl GST</th></tr></thead>
          <tbody>{d.lines.map((l) => (
            <tr key={l.sequence}><td data-label="Item">{l.description}{!l.gstApplicable && <span className="muted small"> (no GST)</span>}</td><td data-label="Qty" className="money">{l.quantity}</td>
              <td data-label="Price" className="money">{money(l.unitPrice)}</td><td data-label="Amount" className="money">{money(l.net)}</td></tr>
          ))}</tbody>
        </table>
        <table className="totals"><tbody>
          <tr><th scope="row">Subtotal</th><td className="money">{money(d.totals.subtotal)}</td></tr>
          <tr><th scope="row">GST {d.gstPercent}%</th><td className="money">{money(d.totals.gst)}</td></tr>
          <tr className="grand"><th scope="row">Total{isInv ? '' : ' (estimate)'}</th><td className="money">{money(d.totals.total)}</td></tr>
          {isInv && <tr><th scope="row">Paid</th><td className="money">{money(d.totals.paid)}</td></tr>}
          {isInv && <tr className="grand"><th scope="row">Balance owing</th><td className="money">{money(d.totals.balance)}</td></tr>}
        </tbody></table>
        {d.notes && <p>{d.notes}</p>}
        {isInv && settings?.bankAccount && <p className="small">Pay by internet banking to {settings.bankAccount}, reference {d.number ?? ''}.</p>}
        {!isInv && <p className="small muted">This is an estimate only. The final account may differ if the procedure changes.</p>}
        {d.voidReason && <p className="error">Void: {d.voidReason}</p>}
      </section>

      <section className="card no-print">
        <h2>Fee split (internal)</h2>
        <dl className="facts"><dt>Surgeon</dt><dd>{money(d.totals.surgeonShare)}</dd><dt>Anaesthetist</dt><dd>{money(d.totals.anaesthetistShare)}</dd><dt>Facility</dt><dd>{money(d.totals.facilityShare)}</dd></dl>
        <p className="hint">Excluding GST. Procedure fees only; facility fees and consumables go to the facility.</p>
      </section>

      {isInv && mod('claims') && <InvoiceClaim invoiceId={d.id} payer={d.payer} payerName={d.payerName} status={d.status} />}
      {isInv && mod('payments') && [Perm.OnlinePayView, Perm.OnlinePayManage].some(can) && d.payer === 'SelfFunded' && d.status !== 'Draft' && d.status !== 'Void' && <InvoicePayOnline invoiceId={d.id} status={d.status} onChange={() => void load()} />}
      {isInv && mod('claims') && can(Perm.PaymentsTake) && ['Issued', 'PartPaid'].includes(d.status) && <TakePayment invoiceId={d.id} balance={d.totals.balance} onPaid={() => void load()} />}

      {isInv && d.payments.length > 0 && (
        <section className="card"><h2>Payments</h2>
          <ul className="plain">{d.payments.map((x, i) => <li key={i}>{formatDate(x.paidOn)} · {money(x.amount)} · {x.method}{x.reference ? ` · ${x.reference}` : ''} <span className="muted small">(recorded by {x.recordedBy})</span></li>)}</ul></section>
      )}

      {manage && (
        <section className="card no-print stack">
          <h2>Actions</h2>
          <div className="row">
            {d.status === 'Draft' && <button className="btn btn-primary" onClick={() => status('Issued')}>{isInv ? 'Issue invoice' : 'Issue estimate'}</button>}
            {!isInv && d.status === 'Issued' && <><button className="btn btn-light" onClick={() => status('Accepted')}>Patient accepted</button><button className="btn btn-light" onClick={() => status('Declined')}>Patient declined</button></>}
            {!isInv && ['Draft', 'Issued', 'Accepted'].includes(d.status) && <button className="btn btn-primary" onClick={() => act(async () => { const r = await api<{ id: string }>(`/api/billing/estimates/${id}/invoice`, { body: {} }); navigate(`/billing/${r.id}`); })}>Create invoice from estimate</button>}
            {(isInv ? ['Draft', 'Issued'] : ['Draft', 'Issued', 'Accepted']).includes(d.status) && d.payments.length === 0 && <button className="btn btn-link danger" onClick={() => status('Void', true)}>Void</button>}
            <button className="btn btn-light" onClick={() => window.print()}>Print</button>
          </div>
          {isInv && ['Issued', 'PartPaid'].includes(d.status) && (
            <div className="form-grid">
              <label>Date paid<input type="date" value={pay.paidOn} onChange={(e) => setPay({ ...pay, paidOn: e.target.value })} /></label>
              <label>Amount<input type="number" step="0.01" value={pay.amount} onChange={(e) => setPay({ ...pay, amount: e.target.value })} placeholder={String(d.totals.balance)} /></label>
              <label>Method<select value={pay.method} onChange={(e) => setPay({ ...pay, method: e.target.value })}>{[['Eftpos', 'EFTPOS'], ['CreditCard', 'Credit card'], ['BankTransfer', 'Bank transfer'], ['Insurer', 'Insurer'], ['Acc', 'ACC'], ['Cash', 'Cash'], ['Other', 'Other']].map(([v, t]) => <option key={v} value={v}>{t}</option>)}</select></label>
              <label>Reference<input value={pay.reference} onChange={(e) => setPay({ ...pay, reference: e.target.value })} /></label>
              <div className="row"><button className="btn btn-primary" onClick={() => act(async () => { await api(`/api/billing/invoices/${id}/payments`, { body: { ...pay, amount: Number(pay.amount || d.totals.balance) } }); setPay({ ...pay, amount: '', reference: '' }); })}>Record payment</button></div>
            </div>
          )}
        </section>
      )}
    </div>
  );
}

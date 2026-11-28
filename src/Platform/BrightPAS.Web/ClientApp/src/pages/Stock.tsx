import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router';
import { api, downloadPost, errText, formatDate, money, nzDateTime, Perm, PatientSummary, todayIso, addDays } from '../api';
import { useSession } from '../App';
import PatientPicker from '../components/PatientPicker';
import Modal from '../components/Modal';

/** Consumables and implants, implant records for recalls, and purchasing (orders, goods receipt, invoices, Xero bills). */
export default function Stock() {
  const [params, setParams] = useSearchParams();
  const tab = params.get('tab') ?? 'stock';
  const [error, setError] = useState<string | null>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const act = async (fn: () => Promise<any>, ok?: string | ((r: any) => string)) => {
    setError(null); setMsg(null);
    try { const r = await fn(); if (ok) setMsg(typeof ok === 'function' ? ok(r) : ok); return r ?? true; } catch (x: any) { setError(x?.body?.errors?.join(' ') ?? errText(x)); return null; }
  };
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Stock, implants &amp; purchasing</h1><p className="muted">Stock levels, lots and expiry; implants recorded against patients for recalls; orders, goods receipt and supplier invoices for Xero.</p></div>
      {error && <p className="error" role="alert">{error}</p>}{msg && <p className="ok-text" role="status">{msg}</p>}
      <section className="card stack">
        <div className="tabs" role="tablist">{[['stock', 'Stock'], ['implants', 'Implants'], ['orders', 'Orders']].map(([k, t]) =>
          <button key={k} role="tab" aria-selected={tab === k} className={tab === k ? 'on' : ''} onClick={() => setParams({ tab: k })}>{t}</button>)}</div>
        {tab === 'stock' && <StockList act={act} />}
        {tab === 'implants' && <Implants act={act} />}
        {tab === 'orders' && <Orders act={act} />}
      </section>
    </div>
  );
}

type Act = (fn: () => Promise<any>, ok?: string | ((r: any) => string)) => Promise<any>;

function StockList({ act }: { act: Act }) {
  const { can } = useSession();
  const [rows, setRows] = useState<any[] | null>(null);
  const [low, setLow] = useState(false);
  const [suppliers, setSuppliers] = useState<any[]>([]);
  const [edit, setEdit] = useState<any>(null);
  const [sel, setSel] = useState<any>(null);
  const [lots, setLots] = useState<any[]>([]);
  const [use, setUse] = useState({ quantity: '1', reason: '' });
  const load = () => api<any[]>(`/api/assets/stock${low ? '?low=true' : ''}`).then(setRows).catch(() => setRows([]));
  useEffect(() => { load(); api<any[]>('/api/assets/suppliers').then(setSuppliers).catch(() => undefined); }, [low]); // eslint-disable-line react-hooks/exhaustive-deps
  const open = async (r: any) => { setSel(r); setLots(await api(`/api/assets/stock/${r.id}/lots`)); };
  const save = async () => {
    if (await act(() => api('/api/assets/stock', { body: { ...edit, id: edit.id ?? null, supplierId: edit.supplierId || null, reorderLevel: Number(edit.reorderLevel), reorderQuantity: Number(edit.reorderQuantity), unitCost: Number(edit.unitCost) } }), 'Saved.')) { setEdit(null); load(); }
  };
  const f = (k: string, label: string, type = 'text') => <label>{label}<input type={type} value={edit[k] ?? ''} onChange={(e) => setEdit({ ...edit, [k]: e.target.value })} /></label>;
  return (
    <div className="stack">
      <div className="row"><label className="check"><input type="checkbox" checked={low} onChange={(e) => setLow(e.target.checked)} /> Only items at or below reorder level</label>
        {can(Perm.StockManage) && <button className="btn btn-light" onClick={async () => { const r = await act(() => api('/api/assets/orders/reorder', { body: {} }), (x) => x.created.length ? `Draft orders: ${x.created.join('; ')}. Approve them on Orders.` : 'Nothing needs reordering.'); if (r) load(); }}>Draft orders for low stock</button>}
        {can(Perm.AssetsManage) && <button className="btn btn-light" onClick={() => setEdit({ code: '', name: '', unit: 'each', isImplant: false, supplierId: '', supplierCode: '', reorderLevel: 0, reorderQuantity: 0, unitCost: 0, accountCode: '', isActive: true })}>Add stock item</button>}</div>
      {edit && <Modal title={edit.id ? `Edit ${edit.name}` : 'New stock item'} onClose={() => setEdit(null)}><div className="form-grid">
        {f('code', 'Stock code')}{f('name', 'Name')}{f('unit', 'Unit (each, box…)')}
        <label>Supplier<select value={edit.supplierId ?? ''} onChange={(e) => setEdit({ ...edit, supplierId: e.target.value })}><option value="">—</option>{suppliers.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}</select></label>
        {f('supplierCode', "Supplier's product code")}{f('reorderLevel', 'Reorder when down to', 'number')}{f('reorderQuantity', 'Order quantity', 'number')}{f('unitCost', 'Unit cost ($ ex GST)', 'number')}{f('accountCode', 'Xero account code')}
        <label className="check"><input type="checkbox" checked={edit.isImplant} onChange={(e) => setEdit({ ...edit, isImplant: e.target.checked })} /> Implant (recorded against the patient)</label>
        <label className="check"><input type="checkbox" checked={edit.isActive} onChange={(e) => setEdit({ ...edit, isActive: e.target.checked })} /> Active</label></div>
        <div className="row"><button className="btn btn-primary" onClick={save}>Save</button><button className="btn btn-light" onClick={() => setEdit(null)}>Cancel</button></div></Modal>}
      {!rows ? <p>Loading…</p> : rows.length === 0 ? <p className="muted">No stock items.</p> : (
        <table className="results"><thead><tr><th>Item</th><th>On hand</th><th>On order</th><th>Reorder at</th><th>Next expiry</th></tr></thead>
          <tbody>{rows.map((r) => <tr key={r.id} className={sel?.id === r.id ? 'selected' : ''}><td><button className="btn btn-link" onClick={() => open(r)}>{r.code} {r.name}</button><div className="small muted">{r.supplier}{r.isImplant && ' · implant'}</div></td>
            <td>{r.onHand} {r.unit}{r.belowReorder && <> <span className="pill bad">Low</span></>}</td><td>{r.onOrder || ''}</td><td>{r.reorderLevel || '—'}</td><td>{r.nextExpiry ? formatDate(r.nextExpiry) : '—'}</td></tr>)}</tbody></table>)}
      {sel && <div className="card subtle stack">
        <div className="row"><h2>{sel.code} {sel.name}</h2>{can(Perm.AssetsManage) && <button className="btn btn-light btn-small" onClick={() => setEdit({ ...sel, supplierId: sel.supplierId ?? '' })}>Edit</button>}</div>
        {lots.length === 0 ? <p className="muted">None in stock.</p> : <table className="results compact"><thead><tr><th>Lot</th><th>Expiry</th><th>On hand</th><th>Received</th></tr></thead>
          <tbody>{lots.map((l) => <tr key={l.id}><td>{l.lot}</td><td>{l.expiry ? formatDate(l.expiry) : '—'}</td><td>{l.onHand}</td><td>{formatDate(l.receivedOn)}</td></tr>)}</tbody></table>}
        {can(Perm.StockManage) && !sel.isImplant && <div className="row"><label>Quantity used<input type="number" value={use.quantity} onChange={(e) => setUse({ ...use, quantity: e.target.value })} /></label>
          <label>Reason / case<input value={use.reason} onChange={(e) => setUse({ ...use, reason: e.target.value })} /></label>
          <button className="btn btn-light" onClick={async () => { if (await act(() => api('/api/assets/stock/use', { body: { stockItemId: sel.id, quantity: Number(use.quantity), reason: use.reason || null } }), 'Stock taken (earliest expiry first).')) { load(); open(sel); } }}>Take from stock</button></div>}
        {sel.isImplant && <p className="hint">Implants are taken from stock when recorded against a patient (Implants tab).</p>}
      </div>}
    </div>
  );
}

function Implants({ act }: { act: Act }) {
  const { can } = useSession();
  const [items, setItems] = useState<any[]>([]);
  const [patient, setPatient] = useState<PatientSummary | null>(null);
  const [rows, setRows] = useState<any[] | null>(null);
  const [form, setForm] = useState<any>({ stockItemId: '', lot: '', serialNumber: '', manufacturer: '', site: '', implantedOn: todayIso() });
  const [recall, setRecall] = useState({ stockItemId: '', lot: '' });
  useEffect(() => { api<any[]>('/api/assets/stock').then((s) => setItems(s.filter((x) => x.isImplant))).catch(() => undefined); }, []);
  useEffect(() => { if (patient) api<any[]>(`/api/assets/implants?patientId=${patient.id}`).then(setRows).catch(() => setRows([])); }, [patient]);
  const record = async () => {
    if (!patient) return;
    if (await act(() => api('/api/assets/implants', { body: { ...form, patientId: patient.id, lotId: null, bookingId: null, serialNumber: form.serialNumber || null, manufacturer: form.manufacturer || null, site: form.site || null } }), 'Implant recorded.'))
      setRows(await api(`/api/assets/implants?patientId=${patient.id}`));
  };
  const findRecall = async () => { const r = await act(() => api(`/api/assets/implants?stockItemId=${recall.stockItemId}${recall.lot ? `&lot=${encodeURIComponent(recall.lot)}` : ''}`)); if (r) setRows(r); };
  return (
    <div className="stack">
      <PatientPicker value={patient} onChange={setPatient} />
      {patient && can(Perm.StockManage) && <div className="card subtle stack"><h2>Record an implant for {patient.familyName.toUpperCase()}, {patient.givenNames}</h2><div className="form-grid">
        <label>Implant<select value={form.stockItemId} onChange={(e) => setForm({ ...form, stockItemId: e.target.value })}><option value="">Choose…</option>{items.map((i) => <option key={i.id} value={i.id}>{i.code} {i.name}</option>)}</select></label>
        <label>Lot / batch<input value={form.lot} onChange={(e) => setForm({ ...form, lot: e.target.value })} /></label>
        <label>Serial number<input value={form.serialNumber} onChange={(e) => setForm({ ...form, serialNumber: e.target.value })} /></label>
        <label>Manufacturer<input value={form.manufacturer} onChange={(e) => setForm({ ...form, manufacturer: e.target.value })} /></label>
        <label>Site<input value={form.site} onChange={(e) => setForm({ ...form, site: e.target.value })} placeholder="e.g. Left inguinal" /></label>
        <label>Date<input type="date" value={form.implantedOn} onChange={(e) => setForm({ ...form, implantedOn: e.target.value })} /></label></div>
        <div><button className="btn btn-primary" onClick={record}>Record implant</button></div></div>}
      <div className="card subtle row"><label>Recall – implant<select value={recall.stockItemId} onChange={(e) => setRecall({ ...recall, stockItemId: e.target.value })}><option value="">Choose…</option>{items.map((i) => <option key={i.id} value={i.id}>{i.code} {i.name}</option>)}</select></label>
        <label>Lot (optional)<input value={recall.lot} onChange={(e) => setRecall({ ...recall, lot: e.target.value })} /></label>
        <button className="btn btn-light" disabled={!recall.stockItemId} onClick={findRecall}>Find patients</button></div>
      {rows && (rows.length === 0 ? <p className="muted">No implants recorded.</p> : (
        <table className="results"><thead><tr><th>Date</th><th>Patient</th><th>Implant</th><th>Lot / serial</th><th>Site</th></tr></thead>
          <tbody>{rows.map((r) => <tr key={r.id}><td>{formatDate(r.implantedOn)}</td><td><a href={`/patients/${r.patientId}`}>{r.patient}</a></td><td>{r.item}<div className="small muted">{r.manufacturer}</div></td><td>{r.lot}{r.serialNumber && ` / ${r.serialNumber}`}</td><td>{r.site}</td></tr>)}</tbody></table>))}
    </div>
  );
}

function Orders({ act }: { act: Act }) {
  const { can } = useSession();
  const [rows, setRows] = useState<any[] | null>(null);
  const [sel, setSel] = useState<any>(null);
  const [receipt, setReceipt] = useState<Record<string, { quantity: string; lot: string; expiry: string }>>({});
  const [inv, setInv] = useState({ invoiceNumber: '', invoiceDate: todayIso(), dueDate: addDays(todayIso(), 20), amountExGst: '' });
  const [accept, setAccept] = useState('');
  const load = async () => { const r = await api<any[]>('/api/assets/orders?all=true'); setRows(r); if (sel) setSel(r.find((x) => x.id === sel.id) ?? null); };
  useEffect(() => { load().catch(() => setRows([])); }, []); // eslint-disable-line react-hooks/exhaustive-deps
  const status = async (to: string) => { if (await act(() => api(to === 'Approved' ? `/api/assets/orders/${sel.id}/approve` : `/api/assets/orders/${sel.id}/status`, { body: { to } }), `Order ${sel.number}: ${to.toLowerCase()}.`)) await load(); };
  const receive = async () => {
    const lines = Object.entries(receipt).filter(([, v]) => Number(v.quantity) > 0).map(([lineId, v]) => ({ lineId, quantity: Number(v.quantity), lot: v.lot, expiry: v.expiry || null }));
    if (await act(() => api(`/api/assets/orders/${sel.id}/receive`, { body: { lines } }), 'Goods received into stock.')) { setReceipt({}); await load(); }
  };
  const invoice = async () => { if (await act(() => api(`/api/assets/orders/${sel.id}/invoices`, { body: { ...inv, amountExGst: Number(inv.amountExGst) } }), (r) => r.matchNote)) await load(); };
  const xero = async () => { try { const name = await downloadPost('/api/assets/xero/bills', { markExported: true }); act(async () => name, `Downloaded ${name} – import it in Xero (Bills to pay › Import).`); await load(); } catch (x: any) { act(async () => { throw x; }); } };
  return (
    <div className="stack">
      {can(Perm.PurchasingApprove) && <div><button className="btn btn-light" onClick={xero}>Download matched invoices for Xero</button></div>}
      {!rows ? <p>Loading…</p> : rows.length === 0 ? <p className="muted">No orders yet. Use "Draft orders for low stock" on the Stock tab.</p> : (
        <table className="results"><thead><tr><th>Order</th><th>Supplier</th><th>Status</th><th>Total ex GST</th><th>Created</th></tr></thead>
          <tbody>{rows.map((o) => <tr key={o.id} className={sel?.id === o.id ? 'selected' : ''}><td><button className="btn btn-link" onClick={() => setSel(o)}>{o.number}</button></td><td>{o.supplier}</td>
            <td>{o.status === 'PartReceived' ? 'Part received' : o.status}</td><td>{money(o.totalExGst)}</td><td>{nzDateTime(o.createdAt)} · {o.createdBy}</td></tr>)}</tbody></table>)}
      {sel && <div className="card subtle stack">
        <h2>{sel.number} – {sel.supplier}</h2>
        <table className="results compact"><thead><tr><th>Item</th><th>Ordered</th><th>Unit cost</th><th>Received</th>{['Approved', 'Sent', 'PartReceived'].includes(sel.status) && can(Perm.StockManage) && <th>Receive now</th>}</tr></thead>
          <tbody>{sel.items.map((l: any) => { const r = receipt[l.id] ?? { quantity: '', lot: '', expiry: '' }; return <tr key={l.id}><td>{l.code} {l.name}</td><td>{l.quantity}</td><td>{money(l.unitCost)}</td><td>{l.quantityReceived}</td>
            {['Approved', 'Sent', 'PartReceived'].includes(sel.status) && can(Perm.StockManage) && <td><div className="row">
              <input aria-label={`Quantity received of ${l.name}`} type="number" value={r.quantity} onChange={(e) => setReceipt({ ...receipt, [l.id]: { ...r, quantity: e.target.value } })} placeholder="Qty" />
              <input aria-label={`Lot of ${l.name}`} value={r.lot} onChange={(e) => setReceipt({ ...receipt, [l.id]: { ...r, lot: e.target.value } })} placeholder="Lot" />
              <input aria-label={`Expiry of ${l.name}`} type="date" value={r.expiry} onChange={(e) => setReceipt({ ...receipt, [l.id]: { ...r, expiry: e.target.value } })} /></div></td>}</tr>; })}</tbody></table>
        <div className="row">
          {sel.status === 'Draft' && can(Perm.PurchasingApprove) && <button className="btn btn-primary" onClick={() => status('Approved')}>Approve order</button>}
          {sel.status === 'Approved' && can(Perm.StockManage) && <button className="btn btn-light" onClick={() => status('Sent')}>Mark as sent to supplier</button>}
          {['Approved', 'Sent', 'PartReceived'].includes(sel.status) && can(Perm.StockManage) && <button className="btn btn-primary" onClick={receive}>Receive goods</button>}
          {['Draft', 'Approved', 'Sent'].includes(sel.status) && can(Perm.StockManage) && <button className="btn btn-light" onClick={() => status('Cancelled')}>Cancel order</button>}
        </div>
        {sel.invoices.length > 0 && <ul className="plain">{sel.invoices.map((i: any) => <li key={i.id}>Invoice {i.invoiceNumber} {money(i.amountExGst)} ex GST – {i.matched ? <span className="pill green">Matched</span> : <span className="pill bad">Not matched</span>} {i.matchNote}
          {i.exportedToXeroAt && ` · sent to Xero ${nzDateTime(i.exportedToXeroAt)}`}
          {!i.matched && can(Perm.PurchasingApprove) && <span className="row"><input aria-label="Why the difference is accepted" value={accept} onChange={(e) => setAccept(e.target.value)} placeholder="Why the difference is OK" />
            <button className="btn btn-link" onClick={async () => { if (await act(() => api(`/api/assets/invoices/${i.id}/accept`, { body: { reason: accept } }), 'Difference accepted.')) await load(); }}>Accept difference</button></span>}</li>)}</ul>}
        {['PartReceived', 'Received'].includes(sel.status) && can(Perm.PurchasingApprove) && <div className="form-grid">
          <label>Supplier invoice number<input value={inv.invoiceNumber} onChange={(e) => setInv({ ...inv, invoiceNumber: e.target.value })} /></label>
          <label>Invoice date<input type="date" value={inv.invoiceDate} onChange={(e) => setInv({ ...inv, invoiceDate: e.target.value })} /></label>
          <label>Due date<input type="date" value={inv.dueDate} onChange={(e) => setInv({ ...inv, dueDate: e.target.value })} /></label>
          <label>Total excluding GST ($)<input type="number" value={inv.amountExGst} onChange={(e) => setInv({ ...inv, amountExGst: e.target.value })} /></label>
          <div><button className="btn btn-primary" onClick={invoice}>Record and match invoice</button></div></div>}
      </div>}
    </div>
  );
}

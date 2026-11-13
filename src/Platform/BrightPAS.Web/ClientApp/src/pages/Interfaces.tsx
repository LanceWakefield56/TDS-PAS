import { useState } from 'react';
import { api, errText } from '../api';
import { useSession } from '../App';

/** Integration administrator tools. Results normally arrive automatically over MLLP; this page imports a message by hand for testing. */
export default function Interfaces() {
  const { writable } = useSession();
  const [hl7, setHl7] = useState('');
  const [s, setS] = useState({ nhi: 'ZSC21TN', family: 'Ngātai', given: 'Aroha', dob: '1980-05-17' });
  const [msg, setMsg] = useState<string | null>(null);
  const [ack, setAck] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const sample = async () => {
    setError(null);
    try {
      const r = await api<{ message: string }>(`/api/labs/sample?nhi=${encodeURIComponent(s.nhi)}&family=${encodeURIComponent(s.family)}&given=${encodeURIComponent(s.given)}&dob=${s.dob}`);
      setHl7(r.message.split('\r').join('\n'));
    } catch (e) { setError(errText(e)); }
  };
  const send = async () => {
    setMsg(null); setError(null); setAck(null);
    try {
      const r = await api<{ outcome: string; reports: string[]; ack: string }>('/api/labs/import', { body: { message: hl7.split('\n').join('\r') } });
      setMsg(`${r.outcome}: ${r.reports.length} report(s) filed.`); setAck(r.ack.split('\r').join('\n')); setHl7('');
    } catch (e: any) { setError(errText(e)); if (e?.body?.ack) setAck(e.body.ack.split('\r').join('\n')); }
  };
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Lab test import</h1><p className="muted">Lab results normally arrive automatically from the laboratory (HL7 over MLLP). Use this page to import a message by hand or to test. Synthetic test data only.</p></div>
      {!writable('labs') && <p className="warn-box">The Lab results module is not licensed or is read-only, so messages cannot be imported.</p>}
      <section className="card stack">
        <h2>Make a sample lab result</h2>
        <div className="form-grid">
          <label>NHI<input value={s.nhi} onChange={(e) => setS({ ...s, nhi: e.target.value.toUpperCase() })} /></label>
          <label>Family name<input value={s.family} onChange={(e) => setS({ ...s, family: e.target.value })} /></label>
          <label>Given name<input value={s.given} onChange={(e) => setS({ ...s, given: e.target.value })} /></label>
          <label>Date of birth<input type="date" value={s.dob} onChange={(e) => setS({ ...s, dob: e.target.value })} /></label>
        </div>
        <div><button className="btn btn-light" onClick={sample}>Fill in a sample message</button></div>
      </section>
      <section className="card stack">
        <h2>Import an HL7 message</h2>
        <textarea className="mono" value={hl7} onChange={(e) => setHl7(e.target.value)} placeholder="MSH|^~\&|..." aria-label="HL7 message" />
        {msg && <p className="notice">{msg}</p>}
        {error && <p className="error">{error}</p>}
        {ack && <><p className="hint">Reply sent back to the lab (ACK):</p><pre className="mono-box">{ack}</pre></>}
        <div><button className="btn btn-primary" disabled={!hl7.trim() || !writable('labs')} onClick={send}>Import message</button></div>
      </section>
      <section className="card subtle">
        <h2>Automatic connection (MLLP)</h2>
        <p>To receive results automatically, set <code>Labs:Mllp:Port</code> (for example 2575) and <code>Labs:Mllp:AllowedClients</code> (the lab's IP address) in the server settings, and allow that port through the firewall from the lab's address only. See the installation guide.</p>
      </section>
    </div>
  );
}

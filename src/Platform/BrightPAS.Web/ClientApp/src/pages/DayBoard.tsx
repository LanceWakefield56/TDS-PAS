import { useCallback, useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { addDays, api, errText, formatDate, Perm, todayIso } from '../api';
import { useSession } from '../App';

interface Card { bookingId: string; patientId: string; patient: string; nhi: string | null; procedure: string; anaesthetic: string; start: string; column: string; status: string;
  triage: string | null; precautions: string | null; alert: string | null; episodeId: string | null }

// Next move for each column, through the theatre booking status (the clinical record keeps its own detail).
const MOVE: Record<string, { to: string; label: string } | undefined> = {
  'Coming in': { to: 'Arrived', label: 'Arrived' }, Admitted: { to: 'InTheatre', label: 'To theatre' }, 'In theatre': { to: 'Recovery', label: 'To recovery' }, Recovery: { to: 'Discharged', label: 'Gone home' },
};

/** The day of surgery on one screen: every patient in a column for where they are, one tap to move them on. */
export default function DayBoard() {
  const { can, mod, writable } = useSession();
  const [params, setParams] = useSearchParams();
  const date = params.get('date') ?? todayIso();
  const [cols, setCols] = useState<string[]>([]);
  const [cards, setCards] = useState<Card[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const emr = mod('emr') && can(Perm.EmrView);
  const canMove = can(Perm.TheatreBook) && writable('theatre');
  const load = useCallback(async () => {
    try { const r = await api<{ columns: string[]; cards: Card[] }>(`/api/journey/day?date=${date}`); setCols(r.columns); setCards(r.cards); } catch (e) { setError(errText(e)); }
  }, [date]);
  useEffect(() => { void load(); const t = window.setInterval(() => void load(), 30000); return () => window.clearInterval(t); }, [load]);
  const move = async (c: Card, to: string) => {
    setError(null);
    try { await api(`/api/theatre/bookings/${c.bookingId}/status`, { body: { status: to } }); await load(); } catch (e) { setError(errText(e)); }
  };
  const go = (d: string) => setParams({ date: d });
  return (
    <div className="stack-lg">
      <div className="page-head row-between">
        <div><h1>Theatre day</h1><p className="muted">{formatDate(date)} · updates every 30 seconds</p></div>
        <div className="row">
          <button className="btn btn-light" onClick={() => go(addDays(date, -1))}>‹ Previous day</button>
          {date !== todayIso() && <button className="btn btn-light" onClick={() => go(todayIso())}>Today</button>}
          <button className="btn btn-light" onClick={() => go(addDays(date, 1))}>Next day ›</button>
          <Link className="btn btn-light" to={`/theatre?date=${date}`}>Theatre lists</Link>
        </div>
      </div>
      {error && <p className="error" role="alert">{error}</p>}
      {!cards ? <p>Loading…</p> : cards.length === 0 ? <section className="card"><p className="muted">No patients booked on this day.</p></section> : (
        <div className="board" role="list">
          {cols.map((col) => {
            const mine = cards.filter((c) => c.column === col);
            return (
              <section key={col} className="board-col" role="listitem" aria-labelledby={`col-${col}`}>
                <h2 id={`col-${col}`}>{col} <span className="count neutral">{mine.length}</span></h2>
                {mine.map((c) => {
                  const m = MOVE[col];
                  const discharge = col === 'Recovery' && emr; // discharge has its own checks in the clinical record
                  return (
                    <article key={c.bookingId} className={`board-card${c.alert ? ' has-alert' : ''}`}>
                      <div className="row-between"><strong><Link to={`/patients/${c.patientId}`}>{c.patient}</Link></strong><span className="muted small">{c.start.slice(0, 5)}</span></div>
                      <div className="small">{c.procedure} · {c.anaesthetic}</div>
                      {c.alert && <div className="small alert-text">⚠ {c.alert}</div>}
                      {c.triage && <div className="small muted">Triage: {c.triage}</div>}
                      {c.precautions && <div className="small muted">{c.precautions.split('\n').join('; ')}</div>}
                      <div className="row wrap">
                        {emr && <Link className="btn btn-light btn-small" to={`/emr/${c.bookingId}`}>Clinical record</Link>}
                        {m && canMove && !discharge && <button className="btn btn-primary btn-small" onClick={() => void move(c, m.to)}>{m.label} ›</button>}
                        {discharge && <Link className="btn btn-primary btn-small" to={`/emr/${c.bookingId}`}>Discharge ›</Link>}
                      </div>
                    </article>
                  );
                })}
              </section>
            );
          })}
        </div>
      )}
    </div>
  );
}

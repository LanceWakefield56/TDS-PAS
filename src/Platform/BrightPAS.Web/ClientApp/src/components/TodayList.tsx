import { useEffect, useState } from 'react';
import { Link } from 'react-router';
import { api, formatDate } from '../api';
import { ActionLink, JAction } from './Journey';

interface Item { group: string; patientId: string; patient: string; nhi: string | null; what: string; why: string | null; due: string | null; action: JAction; urgency: number }
const GROUPS = ['Theatre today', 'Before surgery', 'After surgery'];

/** Everything this person needs to do, most urgent first, grouped by where the patient is in their journey. */
export default function TodayList() {
  const [items, setItems] = useState<Item[] | null>(null);
  const [all, setAll] = useState<Record<string, boolean>>({});
  useEffect(() => { api<Item[]>('/api/journey/today').then(setItems).catch(() => setItems([])); }, []);
  if (!items) return <section className="card span-all"><p className="muted">Loading your list…</p></section>;
  if (items.length === 0) return <section className="card span-all action"><h2>Your list</h2><p>Nothing waiting for you. 🎉</p></section>;
  const first = items[0];
  return (
    <section className="card span-all stack" aria-labelledby="today-h">
      <h2 id="today-h">Your list <span className="count">{items.length}</span></h2>
      <div className="start-here">
        <span className="muted small">Start here</span>
        <div className="row-between">
          <div><strong>{first.what}</strong> – <Link to={`/patients/${first.patientId}`}>{first.patient}</Link>{first.why && <span className="muted"> · {first.why}</span>}</div>
          <ActionLink a={first.action} primary big />
        </div>
      </div>
      {GROUPS.map((g) => {
        const rows = items.filter((i) => i.group === g);
        if (rows.length === 0) return null;
        const shown = all[g] ? rows : rows.slice(0, 8);
        return (
          <div key={g}>
            <h3>{g} <span className="muted small">({rows.length})</span></h3>
            <ul className="todo">
              {shown.map((i, n) => (
                <li key={`${i.patientId}-${i.what}-${n}`} className={i.urgency <= 1 ? 'urgent' : ''}>
                  <div><strong>{i.what}</strong> · <Link to={`/patients/${i.patientId}`}>{i.patient}</Link> <span className="muted small">{i.nhi ?? ''}</span>
                    <div className="small muted">{[i.why, i.due && formatDate(i.due)].filter(Boolean).join(' · ')}</div></div>
                  <ActionLink a={i.action} />
                </li>))}
            </ul>
            {rows.length > 8 && <button className="btn btn-link" onClick={() => setAll({ ...all, [g]: !all[g] })}>{all[g] ? 'Show fewer' : `Show all ${rows.length}`}</button>}
          </div>
        );
      })}
    </section>
  );
}

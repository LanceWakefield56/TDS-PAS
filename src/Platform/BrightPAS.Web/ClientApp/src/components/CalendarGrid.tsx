import { addDays } from '../api';

/** One block on the calendar. Times are "HH:MM" (or "HH:MM:SS"). Colour is a palette index 0–11. */
export interface CalItem { id: string; date: string; start: string; end: string; colour: number; tentative?: boolean; title: string; sub?: string; badge?: string; muted?: boolean; onClick?: () => void }
export interface AllDayItem { id: string; date: string; colour: number; tentative?: boolean; title: string; muted?: boolean; onClick?: () => void }

export const FIRST_HOUR = 6;
export const LAST_HOUR = 21; // grid ends at 21:00
const SLOTS = (LAST_HOUR - FIRST_HOUR) * 4; // 15-minute rows
const MAX_LANES = 12; // side-by-side entries per day; the day column widens so each stays at least 24px (WCAG 2.5.8)

const toSlot = (t: string) => {
  const [h, m] = t.split(':').map(Number);
  return Math.max(0, Math.min(SLOTS, Math.round(((h - FIRST_HOUR) * 60 + m) / 15)));
};
export const weekStart = (iso: string) => { const d = new Date(iso + 'T00:00'); const off = (d.getDay() + 6) % 7; return addDays(iso, -off); };
const dayLabel = (iso: string) => new Date(iso + 'T00:00').toLocaleDateString('en-NZ', { weekday: 'short', day: 'numeric', month: 'short' });

/** Gives overlapping items side-by-side lanes (up to MAX_LANES) within one day, so no item hides another. */
function lanes(items: CalItem[]) {
  const sorted = [...items].sort((a, b) => toSlot(a.start) - toSlot(b.start) || toSlot(b.end) - toSlot(a.end));
  const placed: { item: CalItem; lane: number }[] = [];
  const laneEnds: number[] = [];
  for (const it of sorted) {
    const s = toSlot(it.start);
    let lane = laneEnds.findIndex((e) => e <= s);
    if (lane === -1) { lane = laneEnds.length; laneEnds.push(0); }
    laneEnds[lane] = Math.max(s + 1, toSlot(it.end));
    placed.push({ item: it, lane: Math.min(lane, MAX_LANES - 1) });
  }
  return { placed, count: Math.max(1, Math.min(MAX_LANES, laneEnds.length)) };
}

/**
 * Week/day calendar like TeamUp: an all-day row on top, then 15-minute rows from 06:00 to 21:00.
 * Positions use CSS classes only (the Content-Security-Policy forbids inline styles).
 */
export default function CalendarGrid({ days, items, allDay, onSlot, today }: {
  days: string[]; items: CalItem[]; allDay: AllDayItem[]; onSlot?: (date: string, time: string) => void; today: string;
}) {
  const hours = Array.from({ length: LAST_HOUR - FIRST_HOUR }, (_, i) => FIRST_HOUR + i);
  const byDay = days.map((d) => lanes(items.filter((i) => i.date === d)));
  const widest = Math.max(1, ...byDay.map((l) => l.count));
  const width = widest <= 4 ? 'w1' : widest <= 6 ? 'w2' : widest <= 8 ? 'w3' : widest <= 10 ? 'w4' : 'w5';
  return (
    <div className={`cal cal-days-${days.length} cal-${width}`}>
      <div className="cal-corner" />
      {days.map((d) => <div key={d} className={`cal-dayhead ${d === today ? 'today' : ''}`}>{dayLabel(d)}</div>)}
      <div className="cal-allday-label small muted">All day</div>
      {days.map((d) => (
        <div key={d} className="cal-allday">
          {allDay.filter((a) => a.date === d).sort((x, y) => x.title.localeCompare(y.title)).map((a) => (
            <button key={a.id} type="button" className={`cal-chip cal-c${a.colour % 12} ${a.tentative ? 'tentative' : ''} ${a.muted ? 'muted' : ''}`} onClick={a.onClick} title={a.title}>{a.title}</button>
          ))}
        </div>
      ))}
      <div className="cal-times">{hours.map((h) => <div key={h} className="cal-hour small muted">{h < 12 ? `${h}am` : h === 12 ? '12pm' : `${h - 12}pm`}</div>)}</div>
      {days.map((d, di) => {
        const { placed, count } = byDay[di];
        return (
          <div key={d} className={`cal-day lanes-${count} ${d === today ? 'today' : ''}`}>
            {onSlot && Array.from({ length: SLOTS / 2 }, (_, i) => {
              const mins = FIRST_HOUR * 60 + i * 30;
              const t = `${String(Math.floor(mins / 60)).padStart(2, '0')}:${String(mins % 60).padStart(2, '0')}`;
              // Mouse shortcut only: keyboard and screen-reader users use the page's New button, so the empty-time cells are kept out of the tab order.
              return <button key={i} type="button" tabIndex={-1} aria-hidden="true" title={`New at ${t}`} className={`cal-slot rs-${i * 2 + 1} re-${i * 2 + 3}`} onClick={() => onSlot(d, t)} />;
            })}
            {placed.map(({ item: it, lane }) => {
              const s = toSlot(it.start); const e = Math.max(s + 1, toSlot(it.end));
              return (
                <button key={it.id} type="button" onClick={it.onClick}
                  className={`cal-item cal-c${it.colour % 12} rs-${s + 1} re-${e + 1} col-${lane + 1} ${e - s <= 2 ? 'short' : ''} ${it.tentative ? 'tentative' : ''} ${it.muted ? 'muted' : ''}`}
                  title={`${it.start.slice(0, 5)}–${it.end.slice(0, 5)} ${it.title}${it.sub ? ' · ' + it.sub : ''}`}>
                  <span><span className="cal-time">{it.start.slice(0, 5)}</span>{it.badge && <span className="cal-badge">{it.badge}</span>}</span><strong>{it.title}</strong>{it.sub && <span className="cal-sub">{it.sub}</span>}
                </button>
              );
            })}
          </div>
        );
      })}
    </div>
  );
}

export function CalendarNav({ date, view, onDate, onView }: { date: string; view: 'day' | 'week'; onDate: (d: string) => void; onView: (v: 'day' | 'week') => void }) {
  const step = view === 'week' ? 7 : 1;
  return (
    <div className="row no-print">
      <button className="btn btn-light btn-small" onClick={() => onDate(addDays(date, -step))}>←</button>
      <button className="btn btn-light btn-small" onClick={() => onDate(new Date().toLocaleDateString('en-CA'))}>Today</button>
      <button className="btn btn-light btn-small" onClick={() => onDate(addDays(date, step))}>→</button>
      <input type="date" value={date} onChange={(e) => e.target.value && onDate(e.target.value)} aria-label="Go to date" />
      <div className="tabs compact-tabs">{(['day', 'week'] as const).map((v) => <button key={v} className={view === v ? 'on' : ''} onClick={() => onView(v)}>{v === 'day' ? 'Day' : 'Week'}</button>)}</div>
    </div>
  );
}

export const daysFor = (date: string, view: 'day' | 'week') => view === 'day' ? [date] : Array.from({ length: 7 }, (_, i) => addDays(weekStart(date), i));

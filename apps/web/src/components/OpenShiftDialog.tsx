import { FlaskConical } from 'lucide-react'
import { useEffect, useState, type FormEvent } from 'react'
import type { Isotope } from '../types'
import { DatePicker } from './DatePicker'

type Props = { date: string; onClose: () => void; onDateChange: (date: string) => void; defaults: Record<Isotope, number>; availableIsotopes: Isotope[]; onOpen: (isotope: Isotope, activity: number, measuredAt: string) => Promise<void> }

export function OpenShiftDialog({ date, onClose, onDateChange, defaults, availableIsotopes, onOpen }: Props) {
  const [isotope, setIsotope] = useState<Isotope>(availableIsotopes[0])
  const [activity, setActivity] = useState(defaults[availableIsotopes[0]])
  const [measuredAt, setMeasuredAt] = useState(`${date}T08:00`)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string>()

  useEffect(() => { if (!availableIsotopes.includes(isotope)) setIsotope(availableIsotopes[0]) }, [availableIsotopes, isotope])
  useEffect(() => { setMeasuredAt(`${date}T08:00`); setActivity(defaults[isotope]) }, [date, defaults, isotope])

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setSaving(true); setError(undefined)
    try { await onOpen(isotope, activity, measuredAt) }
    catch (reason) { setError(reason instanceof Error ? reason.message : 'Не удалось открыть смену.') }
    finally { setSaving(false) }
  }

  return <div className="modal-backdrop"><dialog className="dialog shift-open-dialog" open aria-labelledby="open-shift-title"><div className="dialog-title"><div><h2 id="open-shift-title">Открыть смену</h2></div><FlaskConical size={23} /></div><form onSubmit={submit}><p className="dialog-intro">Выберите дату: если смена уже есть, она откроется автоматически.</p><DatePicker value={date} onChange={onDateChange} /><label>Изотоп<select value={isotope} onChange={(event) => setIsotope(event.target.value as Isotope)}>{availableIsotopes.map((item) => <option key={item}>{item}</option>)}</select></label><label>Исходная активность, МБк<input type="number" min="0" value={activity} onChange={(event) => setActivity(Number(event.target.value))} required /></label><label>Время замера<input type="datetime-local" value={measuredAt} onChange={(event) => setMeasuredAt(event.target.value)} required /></label>{error && <p className="login-error">{error}</p>}<div className="dialog-actions"><button className="button button--secondary" type="button" onClick={onClose}>Отмена</button><button className="button" disabled={saving}>{saving ? 'Открытие…' : 'Открыть смену'}</button></div></form></dialog></div>
}

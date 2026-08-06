import { CalendarDays, ChevronLeft, ChevronRight } from 'lucide-react'
import { useEffect, useState } from 'react'

type Props = { value: string; onChange: (value: string) => void; label?: string }

const asValue = (date: Date) => `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
const asDate = (value: string) => new Date(`${value}T12:00:00`)
const dayLabel = new Intl.DateTimeFormat('ru-RU', { day: 'numeric', month: 'long', year: 'numeric' })
const monthLabel = new Intl.DateTimeFormat('ru-RU', { month: 'long', year: 'numeric' })
const weekdays = ['Пн', 'Вт', 'Ср', 'Чт', 'Пт', 'Сб', 'Вс']
export const formatDate = (value: string) => { const [year, month, day] = value.split('-'); return `${day}.${month}.${year}` }

export function DatePicker({ value, onChange, label = 'Выбрать дату смены' }: Props) {
  const [open, setOpen] = useState(false)
  const [month, setMonth] = useState(() => asDate(value))
  useEffect(() => { setMonth(asDate(value)) }, [value])

  const first = new Date(month.getFullYear(), month.getMonth(), 1)
  const start = new Date(month.getFullYear(), month.getMonth(), 1 - ((first.getDay() + 6) % 7))
  const days = Array.from({ length: 42 }, (_, index) => new Date(start.getFullYear(), start.getMonth(), start.getDate() + index))
  const today = asValue(new Date())

  return <div className="date-picker">
    <button className="date-picker-trigger" type="button" aria-label={label} aria-expanded={open} onClick={() => setOpen((current) => !current)}><CalendarDays size={18} /><span>{formatDate(value)}</span></button>
    {open && <div className="date-picker-popover" role="dialog" aria-label="Календарь">
      <div className="date-picker-nav"><button type="button" aria-label="Предыдущий месяц" onClick={() => setMonth((current) => new Date(current.getFullYear(), current.getMonth() - 1, 1))}><ChevronLeft size={17} /></button><strong>{monthLabel.format(month)}</strong><button type="button" aria-label="Следующий месяц" onClick={() => setMonth((current) => new Date(current.getFullYear(), current.getMonth() + 1, 1))}><ChevronRight size={17} /></button></div>
      <div className="date-picker-weekdays">{weekdays.map((day) => <span key={day}>{day}</span>)}</div>
      <div className="date-picker-days">{days.map((day) => { const nextValue = asValue(day); const isSelected = nextValue === value; return <button key={nextValue} type="button" aria-label={dayLabel.format(day)} className={`${day.getMonth() !== month.getMonth() ? 'is-outside ' : ''}${isSelected ? 'is-selected ' : ''}${nextValue === today ? 'is-today' : ''}`} onClick={() => { onChange(nextValue); setOpen(false) }}>{day.getDate()}</button> })}</div>
    </div>}
  </div>
}

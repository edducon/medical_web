import { ScanLine } from 'lucide-react'
import type { PatientPlan, Scanner } from '../types'
import { isotopeConfig } from '../data'

type PlannerProps = { scanners: Scanner[]; plans: PatientPlan[] }
const startOfDay = 8 * 60
const dayLength = 8 * 60

function formatTime(minutes: number) {
  return `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`
}

function TimelineBlock({ plan }: { plan: PatientPlan }) {
  const left = ((plan.startMinutes - startOfDay) / dayLength) * 100
  const width = Math.max((plan.duration / dayLength) * 100, 8)
  return (
    <div
      className="timeline-block"
      style={{ left: `${left}%`, width: `${width}%`, backgroundColor: isotopeConfig[plan.isotope].color }}
      title={`${plan.number}: ${plan.protocol}`}
    >
      <span>{formatTime(plan.startMinutes)} – {formatTime(plan.startMinutes + plan.duration)}</span>
      <strong>{plan.isotope} · {plan.protocol.includes('ПСМА') ? 'ПСМА' : 'Онко-ПЭТ/КТ'}</strong>
    </div>
  )
}

export function Planner({ scanners, plans }: PlannerProps) {
  const hours = Array.from({ length: 9 }, (_, index) => index + 8)
  return (
    <section className="panel planner-panel" aria-labelledby="planner-title">
      <div className="planner-heading">
        <h2 id="planner-title">Оборудование</h2>
        <div className="time-scale" aria-label="Шкала времени">
          {hours.map((hour) => <span key={hour}>{String(hour).padStart(2, '0')}:00</span>)}
        </div>
      </div>
      <div className="planner-rows">
        {scanners.map((scanner) => (
          <div className="planner-row" key={scanner.id}>
            <div className="scanner-label"><ScanLine size={28} /><div><strong>{scanner.name}</strong><span>{scanner.model}</span></div></div>
            <div className="timeline">
              {hours.slice(0, -1).map((hour) => <i key={hour} style={{ left: `${((hour - 8) / 8) * 100}%` }} />)}
              {plans.filter((plan) => plan.scannerId === scanner.id).map((plan) => <TimelineBlock key={plan.id} plan={plan} />)}
            </div>
          </div>
        ))}
      </div>
      <div className="planner-footer"><span><i className="legend legend--f18" />F-18</span><span><i className="legend legend--ga68" />Ga-68</span><em>Часовой пояс: локальный</em></div>
    </section>
  )
}

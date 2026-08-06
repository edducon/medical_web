import type { Isotope } from './types'

export const isotopeConfig: Record<Isotope, { halfLife: number; coefficient: number; defaultActivity: number; color: string }> = {
  'F-18': { halfLife: 109.77, coefficient: 3.7, defaultActivity: 37000, color: '#1769e0' },
  'Ga-68': { halfLife: 68, coefficient: 2.2, defaultActivity: 10000, color: '#0ea5a4' },
}

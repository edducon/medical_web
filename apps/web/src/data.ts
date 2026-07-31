import type { Isotope } from './types'

export const isotopeConfig: Record<Isotope, { halfLife: number; coefficient: number; color: string }> = {
  'F-18': { halfLife: 109.77, coefficient: 3.7, color: '#1769e0' },
  'Ga-68': { halfLife: 68, coefficient: 2.2, color: '#0ea5a4' },
}

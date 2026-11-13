import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import ApprovedDaysExport from '../ApprovedDaysExport.vue'

vi.mock('../../stores/auth', () => ({ useAuthStore: () => ({ getToken: () => 'demo-token' }) }))
vi.mock('vuetify/components/VAlert', () => ({ VAlert: { template: '<div><slot /></div>' } }))
vi.mock('vuetify/components/VBtn', () => ({ VBtn: { template: '<button><slot /></button>' } }))

describe('exportul zilelor aprobate', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it.each([
    ['Calendar (.ics)', '/api/exports/my-approved-days.ics', 'zile-aprobate.ics'],
    ['Excel (.csv)', '/api/exports/my-approved-days.csv', 'zile-aprobate.csv'],
  ])('descarcă fișierul %s cu tokenul utilizatorului', async (label, url, fileName) => {
    const fetchApi = vi.fn().mockResolvedValue({ ok: true, status: 200, blob: async () => new Blob(['x']) })
    vi.stubGlobal('fetch', fetchApi)
    vi.stubGlobal('URL', { createObjectURL: vi.fn(() => 'blob:export'), revokeObjectURL: vi.fn() })
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      expect(this.download).toBe(fileName)
      expect(this.getAttribute('href')).toBe('blob:export')
    })
    const wrapper = mount(ApprovedDaysExport)

    await wrapper.findAll('button').find(button => button.text() === label)!.trigger('click')
    await flushPromises()

    expect(fetchApi).toHaveBeenCalledWith(url, { headers: { Authorization: 'Bearer demo-token' } })
    expect(click).toHaveBeenCalledOnce()
  })

  it('anunță deconectarea dacă sesiunea a expirat', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false, status: 401 }))
    const wrapper = mount(ApprovedDaysExport)

    await wrapper.findAll('button')[0]!.trigger('click')
    await flushPromises()

    expect(wrapper.emitted('unauthorized')).toHaveLength(1)
  })

  it('afișează o eroare dacă descărcarea eșuează', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false, status: 500 }))
    const wrapper = mount(ApprovedDaysExport)

    await wrapper.findAll('button')[1]!.trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('Fișierul nu a putut fi descărcat')
  })
})

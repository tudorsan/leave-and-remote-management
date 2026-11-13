import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import HomeView from '../../views/HomeView.vue'

const auth = vi.hoisted(() => ({ user: { role: 'Employee' }, getToken: () => 'demo-token' }))
vi.mock('vue-router', () => ({ useRouter: () => ({ replace: vi.fn() }) }))
vi.mock('../../stores/auth', () => ({ useAuthStore: () => auth }))
vi.mock('vuetify/components/VAlert', () => ({ VAlert: { template: '<div><slot /></div>' } }))
vi.mock('vuetify/components/VBtn', () => ({ VBtn: { template: '<button><slot /></button>' } }))
vi.mock('vuetify/components/VCard', () => ({ VCard: { template: '<div><slot /></div>' } }))
vi.mock('vuetify/components/VTextField', () => ({
  VTextField: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template: '<input :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
  },
}))
vi.mock('vuetify/components/VTextarea', () => ({
  VTextarea: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template: '<textarea :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
  },
}))
vi.mock('vuetify/components/VSelect', () => ({
  VSelect: { name: 'VSelect', props: ['label', 'items', 'modelValue'], emits: ['update:modelValue'], template: '<div />' },
}))
vi.mock('vuetify/components/VAutocomplete', () => ({
  VAutocomplete: { name: 'VAutocomplete', props: ['label', 'items', 'modelValue'], emits: ['update:modelValue'], template: '<div />' },
}))

Object.assign(HTMLDialogElement.prototype, {
  showModal(this: HTMLDialogElement) { this.open = true },
  close(this: HTMLDialogElement) { this.open = false },
})

afterEach(() => {
  auth.user.role = 'Employee'
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('calendar on Home', () => {
  it('uses the requested year for calendar years below 100', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ year: 1, month: 1, teamId: 1, members: [], events: [] }),
    }))
    const wrapper = mount(HomeView)
    await flushPromises()
    expect(wrapper.get('table').attributes('aria-label')).toBe('Calendar pentru ianuarie 1')
    expect(wrapper.get('tbody tr td:first-child time').attributes('datetime')).toBe('0001-01-01')
    wrapper.unmount()
  })

  it('offers remote editing only for pending requests, moves the single day and keeps the comment', async () => {
    const month = { year: 2026, month: 11, teamId: 1, members: [], events: [] }
    const remote = {
      id: 12, status: 'Pending', canEdit: true, days: ['2026-11-17'],
      comment: 'Comentariu existent', createdAt: '2026-11-10T12:00:00',
    }
    const fetchApi = vi.fn(async (url: string) => ({
      ok: true,
      json: async () => {
        if (url === '/api/remote-requests') return [remote, { ...remote, id: 13, status: 'Approved' }]
        if (url === '/api/leave-requests') return []
        return month
      },
    }))
    vi.stubGlobal('fetch', fetchApi)
    const wrapper = mount(HomeView)
    await flushPromises()
    const button = (label: string) => wrapper.findAll('button').find(item => item.text() === label)!
    await button('Cererile mele').trigger('click')
    await flushPromises()
    expect(wrapper.findAll('button').filter(item => item.text() === 'Editează cererea')).toHaveLength(1)
    await button('Editează cererea').trigger('click')
    await flushPromises()
    expect(wrapper.get('button[aria-label="Zi remote 2026-11-17"]').attributes('aria-pressed')).toBe('true')
    await wrapper.get('button[aria-label="Zi remote 2026-11-19"]').trigger('click')
    expect(wrapper.get('button[aria-label="Zi remote 2026-11-17"]').attributes('aria-pressed')).toBe('false')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(fetchApi).toHaveBeenCalledWith('/api/remote-requests/12', expect.objectContaining({
      method: 'PUT',
      body: JSON.stringify({ days: ['2026-11-19'], comment: 'Comentariu existent' }),
    }))
    wrapper.unmount()
  })

  it('requires a reason and rejects the selected pending remote request', async () => {
    auth.user.role = 'Manager'
    const month = { year: 2026, month: 11, teamId: 1, members: [], events: [] }
    const fetchApi = vi.fn(async (url: string, options?: RequestInit) => ({
      status: url.endsWith('/reject') && options?.body === JSON.stringify({ reason: 'Scurt' }) ? 400 : 200,
      ok: url.endsWith('/reject') ? options?.body !== JSON.stringify({ reason: 'Scurt' }) : true,
      json: async () => {
        if (url.endsWith('/reject')) return { code: 'INVALID_REJECTION_REASON', message: 'Motivul trebuie să conțină minimum 10 caractere.' }
        if (url === '/api/leave-requests/pending') return []
        if (url === '/api/remote-requests/pending') {
          return [{ id: 7, firstName: 'Ana', lastName: 'Test', days: ['2026-11-17'] }]
        }
        return month
      },
    }))
    vi.stubGlobal('fetch', fetchApi)
    const wrapper = mount(HomeView)
    await flushPromises()
    const button = (label: string) => wrapper.findAll('button').find(item => item.text() === label)!
    await button('Cereri în așteptare').trigger('click')
    await flushPromises()
    expect(wrapper.get('.review-title h3').text()).toBe('Ana Test')
    expect(wrapper.get('.review-type').text()).toBe('Lucru remote')
    await button('Respinge').trigger('click')
    await wrapper.get('textarea').setValue('Scurt')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(wrapper.get('[role="alert"]').text()).toBe('Motivul trebuie să conțină minimum 10 caractere.')
    await wrapper.get('textarea').setValue('Este necesară prezența la birou.')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(fetchApi).toHaveBeenCalledWith('/api/remote-requests/7/reject', {
      method: 'PATCH',
      headers: { Authorization: 'Bearer demo-token', 'Content-Type': 'application/json' },
      body: JSON.stringify({ reason: 'Este necesară prezența la birou.' }),
    })
    expect(wrapper.text()).toContain('Cererea a fost respinsă.')
    expect(wrapper.text()).not.toContain('Ana Test · Lucru remote')
    wrapper.unmount()
  })

  it('shows the remote date error from the API and keeps the selected day', async () => {
    const message = 'Ziua 2026-11-09 este în afara intervalului permis: între 0 și 30 zile de acum.'
    const fetchApi = vi.fn()
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          year: 2026, month: 11, teamId: 1, members: [], events: [],
          holidays: [{ date: '2026-11-30', name: 'Sfântul Andrei' }],
        }),
      })
      .mockResolvedValueOnce({ ok: false, status: 400, json: async () => ({ message }) })
    vi.stubGlobal('fetch', fetchApi)
    const wrapper = mount(HomeView)
    await flushPromises()

    await wrapper.get('.new-request-trigger').trigger('click')
    await wrapper.findAll('button').find(button => button.text() === 'Solicită lucru remote')!.trigger('click')
    expect(wrapper.get('.calendar-holiday').text()).toContain('Sfântul Andrei')
    expect(wrapper.get('button[aria-label="Zi remote 2026-11-30"]').attributes('disabled')).toBeUndefined()
    const day = wrapper.get('button[aria-label="Zi remote 2026-11-09"]')
    await day.trigger('click')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.get('[role="alert"]').text()).toBe(message)
    expect(day.attributes('aria-pressed')).toBe('true')
    wrapper.unmount()
  })

  it.each([
    ['Approved', 'Cererea de lucru remote a fost aprobată automat.'],
    ['Pending', 'Cererea de lucru remote a fost trimisă și așteaptă aprobarea.'],
  ])('sends one remote request and displays its %s status', async (status, message) => {
    const month = { year: 2026, month: 11, teamId: 1, members: [], events: [] }
    const fetchApi = vi.fn()
      .mockResolvedValue({ ok: true, json: async () => month })
      .mockResolvedValueOnce({ ok: true, json: async () => month })
      .mockResolvedValueOnce({ ok: true, json: async () => ({ status }) })
    vi.stubGlobal('fetch', fetchApi)
    const wrapper = mount(HomeView)
    await flushPromises()

    await wrapper.get('.new-request-trigger').trigger('click')
    await wrapper.findAll('button').find(button => button.text() === 'Solicită lucru remote')!.trigger('click')
    const day = (date: string) => wrapper.get(`button[aria-label="Zi remote ${date}"]`)
    await day('2026-11-19').trigger('click')
    await day('2026-11-17').trigger('click')
    await day('2026-11-18').trigger('click')
    await day('2026-11-18').trigger('click')
    expect(day('2026-11-17').attributes('aria-pressed')).toBe('true')

    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(fetchApi).toHaveBeenNthCalledWith(2, '/api/remote-requests', {
      method: 'POST',
      headers: { Authorization: 'Bearer demo-token', 'Content-Type': 'application/json' },
      body: JSON.stringify({ days: ['2026-11-17', '2026-11-19'], comment: null }),
    })
    expect(wrapper.text()).toContain(message)
    wrapper.unmount()
  })

  it('cancels only an eligible own request and removes it from the calendar', async () => {
    const month = {
      year: 2026, month: 11, teamId: 1,
      members: [{ id: 1, firstName: 'Ana', lastName: 'Test' }],
      events: [{ requestId: 7, userId: 1, date: '2026-11-16', type: 'Vacation', status: 'Pending' }],
    }
    const pending = { id: 7, startDate: '2026-11-16', endDate: '2026-11-16', type: 'Vacation', status: 'Pending', canCancel: true, createdAt: '2026-11-10T12:00:00' }
    const fetchApi = vi.fn()
      .mockResolvedValueOnce({ ok: true, json: async () => month })
      .mockResolvedValueOnce({ ok: true, json: async () => [
        pending, { ...pending, id: 8, status: 'Approved', canCancel: false },
      ] })
      .mockResolvedValueOnce({ ok: true, json: async () => [{
        id: 7, days: ['2026-11-17'], status: 'Rejected', createdAt: '2026-11-10T13:00:00',
        rejectionReason: 'Este necesară prezența la birou.',
      }] })
      .mockResolvedValueOnce({ ok: true })
      .mockResolvedValueOnce({ ok: true, json: async () => ({ ...month, events: [] }) })
    vi.stubGlobal('fetch', fetchApi)
    const wrapper = mount(HomeView)
    await flushPromises()

    await wrapper.findAll('button').find(button => button.text() === 'Cererile mele')!.trigger('click')
    await flushPromises()
    expect(wrapper.get('.own-request-list').text()).toContain('Motivul respingerii: Este necesară prezența la birou.')
    const cancelButtons = wrapper.findAll('button').filter(button => button.text() === 'Anulează cererea')
    expect(cancelButtons).toHaveLength(1)
    await cancelButtons[0]!.trigger('click')
    expect(fetchApi).toHaveBeenCalledTimes(3)
    await wrapper.get('dialog[open] button:first-child').trigger('click')
    expect(wrapper.find('dialog').exists()).toBe(false)
    expect(fetchApi).toHaveBeenCalledTimes(3)
    await cancelButtons[0]!.trigger('click')
    await wrapper.get('dialog .confirm-primary').trigger('click')
    await flushPromises()

    expect(fetchApi).toHaveBeenNthCalledWith(4, '/api/leave-requests/7/cancel', {
      method: 'PATCH',
      headers: { Authorization: 'Bearer demo-token' },
    })
    expect(wrapper.get('.own-request-list').text()).toContain('Anulată')
    expect(wrapper.find('.new-request-trigger').exists()).toBe(false)
    expect(wrapper.get('.workspace-nav [aria-current="page"]').text()).toBe('Cererile mele')
    expect(wrapper.find('.calendar').exists()).toBe(false)
    await wrapper.get('.panel-navigation button').trigger('click')
    expect(wrapper.find('.new-request-trigger').exists()).toBe(true)
    expect(wrapper.find('.calendar').exists()).toBe(true)
    expect(wrapper.find('tbody .event').exists()).toBe(false)
    wrapper.unmount()
  })

  it('sends a leave request and shows it as pending in the calendar without a page reload', async () => {
    const month = {
      year: 2026, month: 11, teamId: 1,
      members: [{ id: 1, firstName: 'Ana', lastName: 'Test' }],
      events: [],
    }
    const fetchCalendar = vi.fn()
      .mockResolvedValueOnce({ ok: true, json: async () => month })
      .mockResolvedValueOnce({ ok: true, json: async () => ({ workingDays: 2 }) })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          ...month,
          events: [{ requestId: 1, userId: 1, date: '2026-11-16', type: 'Vacation', status: 'Pending' }],
        }),
      })
    vi.stubGlobal('fetch', fetchCalendar)
    const wrapper = mount(HomeView)
    await flushPromises()

    const newRequestMenu = wrapper.get('.new-request-trigger')
    expect(newRequestMenu.attributes('aria-expanded')).toBe('false')
    await newRequestMenu.trigger('click')
    expect(newRequestMenu.attributes('aria-expanded')).toBe('true')
    await wrapper.findAll('button').find(button => button.text() === 'Solicită concediu')!.trigger('click')
    expect(wrapper.find('.calendar').exists()).toBe(true)
    await wrapper.get('input[label="Data de început"]').setValue('2026-11-16')
    await wrapper.get('input[label="Data de sfârșit"]').setValue('2026-11-17')
    const type = wrapper.findAllComponents({ name: 'VSelect' }).find(select => select.props('label') === 'Tip de concediu')!
    type.vm.$emit('update:modelValue', 'Vacation')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(fetchCalendar).toHaveBeenNthCalledWith(2, '/api/leave-requests', {
      method: 'POST',
      headers: { Authorization: 'Bearer demo-token', 'Content-Type': 'application/json' },
      body: JSON.stringify({ startDate: '2026-11-16', endDate: '2026-11-17', type: 'Vacation', comment: null }),
    })
    expect(fetchCalendar).toHaveBeenNthCalledWith(3, '/api/calendar?year=2026&month=11', expect.anything())
    expect(wrapper.text()).toContain('Cererea de concediu a fost trimisă pentru 2 zile lucrătoare.')
    expect(wrapper.find('form').exists()).toBe(false)
    const day = wrapper.get('time[datetime="2026-11-16"]').element.closest('td')
    expect(day?.querySelector('.event')?.textContent).toContain('Concediu de odihnă · În așteptare')
    wrapper.unmount()
  })

  it('keeps both Admin filters when navigating to another month', async () => {
    auth.user.role = 'Admin'
    const fetchCalendar = vi.fn(async (url: string) => {
      const params = new URL(url, 'http://localhost').searchParams
      return {
        ok: true,
        json: async () => url === '/api/teams'
          ? [{ id: 4, name: 'QA' }]
          : { year: 2026, month: Number(params.get('month') ?? 11), teamId: null, members: [], events: [] },
      }
    })
    vi.stubGlobal('fetch', fetchCalendar)
    const wrapper = mount(HomeView)
    await flushPromises()

    const type = wrapper.findAllComponents({ name: 'VSelect' }).find(select => select.attributes('id') === 'calendar-absence-type')!
    await wrapper.get('#calendar-team-trigger').trigger('click')
    await wrapper.get('#calendar-team-search').setValue('qa')
    const teamOptions = wrapper.findAll('#calendar-team-options li button')
    expect(teamOptions.map(option => option.text())).toEqual(['QA'])
    await teamOptions[0]!.trigger('click')
    await flushPromises()
    type.vm.$emit('update:modelValue', 'Remote')
    await flushPromises()
    await wrapper.findAll('button').find(b => b.text() === 'Luna următoare')!.trigger('click')
    await flushPromises()
    expect(fetchCalendar).toHaveBeenLastCalledWith('/api/calendar?year=2026&month=12&teamId=4&type=Remote', expect.anything())
    wrapper.unmount()
  })

  it('requests the selected month across the year boundary in both directions', async () => {
    const month = (year: number, month: number) => ({
      ok: true,
      json: async () => ({ year, month, teamId: 1, members: [], events: [] }),
    })
    const fetchCalendar = vi.fn()
      .mockResolvedValueOnce(month(2026, 12))
      .mockResolvedValueOnce(month(2027, 1))
      .mockResolvedValueOnce(month(2026, 12))
    vi.stubGlobal('fetch', fetchCalendar)
    const wrapper = mount(HomeView)
    await flushPromises()

    const button = (label: string) => wrapper.findAll('button').find(b => b.text() === label)!
    await button('Luna următoare').trigger('click')
    await flushPromises()
    expect(fetchCalendar).toHaveBeenNthCalledWith(2, '/api/calendar?year=2027&month=1', expect.anything())
    expect(wrapper.get('table').attributes('aria-label')).toBe('Calendar pentru ianuarie 2027')

    await button('Luna anterioară').trigger('click')
    await flushPromises()
    expect(fetchCalendar).toHaveBeenNthCalledWith(3, '/api/calendar?year=2026&month=12', expect.anything())
    expect(wrapper.get('table').attributes('aria-label')).toBe('Calendar pentru decembrie 2026')
    wrapper.unmount()
  })

  it('places dated requests in the right cells and includes members without events', async () => {
    const fetchCalendar = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({
        year: 2026,
        month: 11,
        teamId: 1,
        members: [
          { id: 1, firstName: 'Ana', lastName: 'Test' },
          { id: 2, firstName: 'Mihai', lastName: 'Test' },
        ],
        events: [
          { requestId: 1, userId: 1, date: '2026-11-02', type: 'Vacation', status: 'Pending' },
          { requestId: 1, userId: 1, date: '2026-11-03', type: 'Remote', status: 'Approved' },
        ],
      }),
    })
    vi.stubGlobal('fetch', fetchCalendar)
    const wrapper = mount(HomeView)
    await flushPromises()

    expect(fetchCalendar).toHaveBeenCalledWith('/api/calendar', {
      headers: { Authorization: 'Bearer demo-token' },
    })
    expect(wrapper.get('label[for="calendar-absence-type"]').text()).toBe('Tip de absență')
    const membersButton = wrapper.get('button[aria-controls="calendar-member-list"]')
    expect(membersButton.attributes('aria-expanded')).toBe('false')
    expect(wrapper.get<HTMLElement>('#calendar-member-list').element.style.display).toBe('none')
    await membersButton.trigger('click')
    expect(membersButton.attributes('aria-expanded')).toBe('true')
    expect(wrapper.get<HTMLElement>('#calendar-member-list').element.style.display).not.toBe('none')
    expect(wrapper.get('.member-list').text()).toContain('Mihai Test')
    await wrapper.get('#calendar-member-search').setValue('mih')
    expect(wrapper.findAll('.member-list li').map(member => member.text())).toEqual(['Mihai Test'])
    const cells = wrapper.findAll('tbody td')
    expect(cells[6]?.get('time').attributes('datetime')).toBe('2026-11-01')
    expect(cells[7]?.get('.event').text()).toContain('Ana Test')
    expect(cells[7]?.get('.event').text()).toContain('Concediu de odihnă · În așteptare')
    expect(cells[7]?.get('.event').classes()).toContain('bg-blue-lighten-4')
    expect(cells[8]?.get('.event').text()).toContain('Lucru remote · Aprobat')
    expect(cells[8]?.get('.event').classes()).toContain('bg-teal-darken-3')
    expect(wrapper.findAll('.legend li')).toHaveLength(5)
    wrapper.unmount()
  })
})

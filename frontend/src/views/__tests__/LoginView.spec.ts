import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import LoginView from '../LoginView.vue'

const navigation = vi.hoisted(() => ({ query: {} as Record<string, string>, replace: vi.fn() }))
const auth = vi.hoisted(() => ({ startSession: vi.fn() }))

vi.mock('vue-router', () => ({
  useRouter: () => ({ replace: navigation.replace }),
  useRoute: () => ({ query: navigation.query }),
  RouterLink: { template: '<a><slot /></a>' },
}))
vi.mock('../../stores/auth', () => ({ useAuthStore: () => auth }))
vi.mock('vuetify/components/VAlert', () => ({ VAlert: { template: '<div class="alert"><slot /></div>' } }))
vi.mock('vuetify/components/VBtn', () => ({ VBtn: { template: '<button><slot /></button>' } }))
vi.mock('vuetify/components/VCard', () => ({ VCard: { template: '<div><slot /></div>' } }))
vi.mock('vuetify/components/VTextField', () => ({
  VTextField: {
    props: ['modelValue', 'type', 'id'],
    emits: ['update:modelValue'],
    template: `<input :id="id" :type="type" :value="modelValue" @input="$emit('update:modelValue', $event.target.value)" />`,
  },
}))

describe('pagina de autentificare', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    navigation.query = {}
    navigation.replace.mockReset()
    auth.startSession.mockReset()
  })

  it('explică de ce apare din nou login-ul când sesiunea a expirat', () => {
    navigation.query = { session: 'expired' }

    expect(mount(LoginView).text()).toContain('Sesiunea ta a expirat')
  })

  it('nu afișează mesajul de expirare la un login obișnuit', () => {
    expect(mount(LoginView).text()).not.toContain('Sesiunea ta a expirat')
  })

  async function loginWithCard(card: string) {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: async () => ({ token: 'jwt' }) }))
    const wrapper = mount(LoginView)
    await wrapper.findAll('button.role-option').find(button => button.text().includes(card))!.trigger('click')
    await wrapper.get('#login-email').setValue('employee@example.com')
    await wrapper.get('#login-password').setValue('Employee123!')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    return wrapper
  }

  it('cere alegerea rolului corect când cardul nu corespunde contului', async () => {
    auth.startSession.mockResolvedValue('role-mismatch')

    const wrapper = await loginWithCard('Manager')

    expect(auth.startSession).toHaveBeenCalledWith('jwt', 'Manager')
    expect(wrapper.text()).toContain('Contul are alt rol')
    expect(navigation.replace).not.toHaveBeenCalled()
  })

  it('intră în aplicație când cardul corespunde rolului contului', async () => {
    auth.startSession.mockResolvedValue(true)

    await loginWithCard('Angajat')

    expect(auth.startSession).toHaveBeenCalledWith('jwt', 'Employee')
    expect(navigation.replace).toHaveBeenCalledWith({ name: 'home' })
  })
})

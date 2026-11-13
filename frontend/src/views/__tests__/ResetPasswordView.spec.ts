import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createRouter, createMemoryHistory } from 'vue-router'
import ResetPasswordView from '../ResetPasswordView.vue'

// Componentele Vuetify sunt înlocuite cu elemente HTML simple, ca în celelalte teste.
vi.mock('vuetify/components/VAlert', () => ({ VAlert: { template: '<div><slot /></div>' } }))
vi.mock('vuetify/components/VBtn', () => ({ VBtn: { template: '<button><slot /></button>' } }))
vi.mock('vuetify/components/VCard', () => ({ VCard: { template: '<div><slot /></div>' } }))
vi.mock('vuetify/components/VTextField', () => ({
  VTextField: {
    props: ['modelValue', 'type', 'errorMessages'],
    emits: ['update:modelValue'],
    template: `<label>
      <input :type="type" :value="modelValue" @input="$emit('update:modelValue', $event.target.value)" />
      <span v-if="errorMessages && errorMessages.length">{{ errorMessages }}</span>
    </label>`,
  },
}))

async function mountAt(url: string) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/reset-password', name: 'reset-password', component: ResetPasswordView },
      { path: '/forgot-password', name: 'forgot-password', component: { template: '<div />' } },
      { path: '/login', name: 'login', component: { template: '<div />' } },
    ],
  })
  await router.push(url)
  await router.isReady()
  return mount(ResetPasswordView, { global: { plugins: [router] } })
}

describe('pagina de resetare a parolei', () => {
  beforeEach(() => setActivePinia(createPinia()))
  afterEach(() => vi.unstubAllGlobals())

  it('cere un link nou când lipsește tokenul', async () => {
    const wrapper = await mountAt('/reset-password')

    expect(wrapper.text()).toContain('Linkul de resetare este incomplet')
    expect(wrapper.find('form').exists()).toBe(false)
  })

  it('nu trimite parola dacă cele două câmpuri nu coincid', async () => {
    const fetchApi = vi.fn()
    vi.stubGlobal('fetch', fetchApi)
    const wrapper = await mountAt('/reset-password?token=abc')

    const [password, confirmation] = wrapper.findAll('input[type="password"]')
    await password!.setValue('ParolaNoua456!')
    await confirmation!.setValue('AltaParola')
    await wrapper.get('form').trigger('submit')

    expect(wrapper.text()).toContain('Parolele nu coincid.')
    expect(fetchApi).not.toHaveBeenCalled()
  })

  it('trimite tokenul din link împreună cu parola nouă', async () => {
    const fetchApi = vi.fn().mockResolvedValue({ ok: true, json: async () => ({ message: 'ok' }) })
    vi.stubGlobal('fetch', fetchApi)
    const wrapper = await mountAt('/reset-password?token=abc123')

    const [password, confirmation] = wrapper.findAll('input[type="password"]')
    await password!.setValue('ParolaNoua456!')
    await confirmation!.setValue('ParolaNoua456!')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(fetchApi).toHaveBeenCalledWith('/api/auth/reset-password', expect.objectContaining({
      method: 'POST',
      body: JSON.stringify({ token: 'abc123', newPassword: 'ParolaNoua456!' }),
    }))
    expect(wrapper.text()).toContain('Parola a fost schimbată')
  })
})

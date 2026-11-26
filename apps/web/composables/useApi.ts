import createClient from 'openapi-fetch'
import type { paths } from '~/types/api'

export function useApi() {
  const config = useRuntimeConfig()
  return createClient<paths>({ baseUrl: config.public.apiBase })
}

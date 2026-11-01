import { z } from 'zod';

/**
 * Runtime config: window.__APP_CONFIG__ (written by the container from env vars) wins,
 * then Vite build-time VITE_* variables. One image works in every environment.
 */
declare global {
  interface Window {
    __APP_CONFIG__?: Partial<Record<keyof AppConfig, string>>;
  }
}

const schema = z.object({
  entraClientId: z.string().uuid('VITE_ENTRA_CLIENT_ID must be the SPA app registration client id (GUID).'),
  entraAuthority: z.string().url().default('https://login.microsoftonline.com/organizations'),
  apiScope: z.string().min(1, 'VITE_API_SCOPE is required, e.g. api://<api-client-id>/access_as_user.'),
  apiBaseUrl: z.string().min(1).default('/api'),
});

export type AppConfig = z.infer<typeof schema>;

export type ConfigResult = { ok: true; config: AppConfig } | { ok: false; errors: string[] };

type RawConfig = Partial<Record<keyof AppConfig, string | undefined>>;

const blankToUndefined = (v: string | undefined) => (v && v.trim() !== '' ? v.trim() : undefined);

export function parseConfig(raw: RawConfig): ConfigResult {
  const result = schema.safeParse({
    entraClientId: blankToUndefined(raw.entraClientId),
    entraAuthority: blankToUndefined(raw.entraAuthority),
    apiScope: blankToUndefined(raw.apiScope),
    apiBaseUrl: blankToUndefined(raw.apiBaseUrl),
  });
  if (result.success) return { ok: true, config: result.data };
  return { ok: false, errors: result.error.issues.map((i) => `${i.path.join('.')}: ${i.message}`) };
}

export function loadConfig(): ConfigResult {
  const runtime = (typeof window !== 'undefined' && window.__APP_CONFIG__) || {};
  const env = import.meta.env;
  return parseConfig({
    entraClientId: runtime.entraClientId ?? env.VITE_ENTRA_CLIENT_ID,
    entraAuthority: runtime.entraAuthority ?? env.VITE_ENTRA_AUTHORITY,
    apiScope: runtime.apiScope ?? env.VITE_API_SCOPE,
    apiBaseUrl: runtime.apiBaseUrl ?? env.VITE_API_BASE_URL,
  });
}

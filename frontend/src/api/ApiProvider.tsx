import { createContext, useContext, useMemo, type ReactNode } from 'react';
import { createApiClient, type ApiRequest } from './client';
import { getApiToken } from '@/auth/msal';
import { getSelectedCompany, getSelectedTenantId } from '@/state/sessionStore';

const ApiContext = createContext<ApiRequest | null>(null);

export function ApiProvider({ baseUrl, children, client }: { baseUrl: string; children: ReactNode; client?: ApiRequest }) {
  const api = useMemo(
    () => client ?? createApiClient({ baseUrl, getToken: getApiToken, getTenantId: getSelectedTenantId, getCompany: getSelectedCompany }),
    [baseUrl, client],
  );
  return <ApiContext.Provider value={api}>{children}</ApiContext.Provider>;
}

export function useApi(): ApiRequest {
  const api = useContext(ApiContext);
  if (!api) throw new Error('useApi must be used inside <ApiProvider>.');
  return api;
}

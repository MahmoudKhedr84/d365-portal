/** Error carrying the backend's RFC 7807 problem details (code/detail) or a network failure. */
export class ApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly code: string,
    message: string,
    public readonly traceId?: string,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

export async function toApiError(response: Response): Promise<ApiError> {
  let body: unknown = null;
  try {
    const text = await response.text();
    body = text ? JSON.parse(text) : null;
  } catch {
    /* non-JSON body */
  }
  const p = (body ?? {}) as { code?: string; detail?: string; title?: string; traceId?: string };
  const fallback = response.status === 401 ? 'Your session has expired. Please sign in again.' : `Request failed (${response.status}).`;
  return new ApiError(response.status, p.code ?? `http_${response.status}`, p.detail ?? p.title ?? fallback, p.traceId);
}

export interface ApiClientDeps {
  baseUrl: string;
  getToken: () => Promise<string>;
  getTenantId: () => string | null;
  /** Working company inside the tenant; sent as X-Company with tenant-scoped calls. */
  getCompany?: () => string | null;
  fetchImpl?: typeof fetch;
}

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PATCH' | 'PUT' | 'DELETE';
  body?: unknown;
  /** Send X-Tenant-Id (default true when a tenant is selected). */
  tenantScoped?: boolean;
  signal?: AbortSignal;
}

export function createApiClient(deps: ApiClientDeps) {
  const doFetch = deps.fetchImpl ?? ((...args: Parameters<typeof fetch>) => fetch(...args));

  return async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
    const headers: Record<string, string> = {
      Accept: 'application/json',
      Authorization: `Bearer ${await deps.getToken()}`,
    };
    const tenantId = options.tenantScoped === false ? null : deps.getTenantId();
    if (tenantId) {
      headers['X-Tenant-Id'] = tenantId;
      const company = deps.getCompany?.();
      if (company) headers['X-Company'] = company;
    }
    if (options.body !== undefined) headers['Content-Type'] = 'application/json';

    let response: Response;
    try {
      response = await doFetch(`${deps.baseUrl.replace(/\/$/, '')}${path}`, {
        method: options.method ?? 'GET',
        headers,
        body: options.body === undefined ? undefined : JSON.stringify(options.body),
        signal: options.signal,
      });
    } catch (e) {
      if ((e as Error).name === 'AbortError') throw e;
      throw new ApiError(0, 'network_error', 'Cannot reach the server. Check your connection and try again.');
    }

    if (!response.ok) throw await toApiError(response);
    if (response.status === 204) return undefined as T;
    return (await response.json()) as T;
  };
}

export type ApiRequest = ReturnType<typeof createApiClient>;

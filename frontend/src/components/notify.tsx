import { useCallback } from 'react';
import {
  Toast,
  ToastBody,
  ToastTitle,
  Toaster,
  useToastController,
  type ToastIntent,
} from '@fluentui/react-components';
import { ApiError } from '@/api/client';

export const APP_TOASTER_ID = 'app-toaster';

export function AppToaster() {
  return <Toaster toasterId={APP_TOASTER_ID} position="top-end" pauseOnHover />;
}

/** Success/error toasts. Errors show the API's problem detail (the D365 / validation message). */
export function useNotify() {
  const { dispatchToast } = useToastController(APP_TOASTER_ID);

  const show = useCallback(
    (intent: ToastIntent, title: string, body?: string) =>
      dispatchToast(
        <Toast>
          <ToastTitle>{title}</ToastTitle>
          {body && <ToastBody>{body}</ToastBody>}
        </Toast>,
        { intent, timeout: intent === 'error' ? 8000 : 4000 },
      ),
    [dispatchToast],
  );

  return {
    success: (title: string, body?: string) => show('success', title, body),
    error: (title: string, err: unknown) => show('error', title, errorMessage(err)),
  };
}

export function errorMessage(err: unknown): string {
  if (err instanceof ApiError) return err.message;
  if (err instanceof Error) return err.message;
  return 'Unexpected error.';
}

/** Field-level errors from a zod issue list, keyed by top-level field name. */
export function fieldErrors(issues: { path: (string | number)[]; message: string }[]): Record<string, string> {
  const out: Record<string, string> = {};
  for (const i of issues) {
    const key = String(i.path[0] ?? '_');
    out[key] ??= i.message;
  }
  return out;
}

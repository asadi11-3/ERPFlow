import { HttpErrorResponse } from '@angular/common/http';

interface Problem {
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
}

export function problemMessage(err: unknown): string {
  if (!(err instanceof HttpErrorResponse)) return 'Something went wrong. Try again.';
  const body = (err.error ?? {}) as Problem;
  switch (err.status) {
    case 0:
      return err.statusText === 'Offline'
        ? 'You are offline. Reconnect before saving. Nothing has been queued.'
        : 'Connection lost. If you were saving, check the record after reconnecting before trying again.';
    case 400: {
      const first = body.errors ? Object.values(body.errors).flat()[0] : undefined;
      return first ?? body.detail ?? body.title ?? 'The request is not valid.';
    }
    case 403:
      return 'You do not have access to this action.';
    case 404:
      return 'Not found. It may have been removed.';
    case 409:
      return body.detail ?? body.title ?? 'The order changed. Refresh and try again.';
    default:
      return 'Server error. Try again later.';
  }
}

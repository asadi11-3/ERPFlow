import { HttpErrorResponse } from '@angular/common/http';

import { ProductImageContractError } from './product-images';

export function productImageError(error: unknown): string {
  if (error instanceof ProductImageContractError) return error.message;
  if (!(error instanceof HttpErrorResponse)) return 'Could not update the image. Try again.';
  switch (error.status) {
    case 401: return 'Your session expired. Sign in again to view product images.';
    case 403: return 'You do not have permission to change this image.';
    case 404: return 'This product or image is no longer available.';
    case 412: return 'The image changed elsewhere. The latest image has been loaded; review it before trying again.';
    case 413: return 'The image is too large. Choose a file of 5 MiB or less.';
    case 428: return 'The image version is missing. Refresh the image before trying again.';
    case 415: return 'Choose a JPEG, PNG, or WebP with a matching file extension.';
    case 400: {
      const body: unknown = error.error;
      if (typeof body === 'object' && body !== null && 'detail' in body && typeof body.detail === 'string')
        return body.detail;
      return 'The image is invalid or corrupted. Choose a different JPEG, PNG, or WebP.';
    }
    case 0: return 'Connection lost. Reconnect and refresh the image before trying again.';
    default: return 'The image service is unavailable. Refresh before retrying; the last change may have completed.';
  }
}

export function imageFileError(file: File): string | null {
  if (file.size === 0) return 'Choose a nonempty image file.';
  if (file.size > 5 * 1024 * 1024) return 'The image is too large. Choose a file of 5 MiB or less.';
  const extension = file.name.split('.').pop()?.toLowerCase();
  if (!((file.type === 'image/jpeg' && (extension === 'jpg' || extension === 'jpeg')) ||
      (file.type === 'image/png' && extension === 'png') || (file.type === 'image/webp' && extension === 'webp')))
    return 'Choose a JPEG, PNG, or WebP with a matching file extension.';
  return null;
}

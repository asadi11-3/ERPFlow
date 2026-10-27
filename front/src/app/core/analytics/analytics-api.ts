import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../config';
import { AnalyticsKind, AnalyticsResults, AnalyticsWindow } from './models';

@Injectable({ providedIn: 'root' })
export class AnalyticsApi {
  private readonly http = inject(HttpClient);

  read<K extends AnalyticsKind>(kind: K, window: AnalyticsWindow): Promise<AnalyticsResults[K]> {
    return firstValueFrom(this.http.get<AnalyticsResults[K]>(`${API_BASE_URL}/api/analytics/${kind}`, {
      params: { from: window.from, to: window.to, groupBy: window.groupBy },
    }));
  }
}

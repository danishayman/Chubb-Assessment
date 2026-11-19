import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  ClaimDetail, ClaimStatus, ClaimSummary, Exposure, Market, NotificationList, Policy, Problem, Staff, SubmitClaim, Workload,
} from './models';

export interface ClaimFilter {
  status?: ClaimStatus | '';
  assigned?: 'me' | 'unassigned' | '';
  market?: Market | '';
  openOnly?: boolean;
  search?: string;
}

/** Thin promise-based wrapper over the REST API. */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private http = inject(HttpClient);

  myPolicies() { return this.get<Policy[]>('/api/policies/mine'); }
  staff() { return this.get<Staff[]>('/api/staff'); }

  claims(filter: ClaimFilter = {}) {
    let params = new HttpParams();
    for (const [k, v] of Object.entries(filter)) if (v !== undefined && v !== '' && v !== false) params = params.set(k, String(v));
    return firstValueFrom(this.http.get<ClaimSummary[]>('/api/claims', { params }));
  }
  claim(id: string) { return this.get<ClaimDetail>(`/api/claims/${id}`); }
  submit(body: SubmitClaim) { return this.post<ClaimDetail>('/api/claims', body); }
  respond(id: string, requestId: string, response: string) {
    return this.post<ClaimDetail>(`/api/claims/${id}/info-requests/${requestId}/response`, { response });
  }

  assign(id: string, officerId?: string) { return this.post<ClaimDetail>(`/api/claims/${id}/assign`, { officerId: officerId ?? null }); }
  setReserve(id: string, amount: number) { return this.post<ClaimDetail>(`/api/claims/${id}/reserve`, { amount }); }
  requestInfo(id: string, question: string) { return this.post<ClaimDetail>(`/api/claims/${id}/info-requests`, { question }); }
  approve(id: string, amount: number, reason: string) { return this.post<ClaimDetail>(`/api/claims/${id}/approve`, { amount, reason }); }
  reject(id: string, reason: string) { return this.post<ClaimDetail>(`/api/claims/${id}/reject`, { reason }); }
  settle(id: string) { return this.post<ClaimDetail>(`/api/claims/${id}/settle`, {}); }
  addNote(id: string, text: string) { return this.post<ClaimDetail>(`/api/claims/${id}/notes`, { text }); }

  notifications() { return this.get<NotificationList>('/api/notifications'); }
  markRead(id: string) { return this.post<void>(`/api/notifications/${id}/read`, {}); }
  markAllRead() { return this.post<void>('/api/notifications/read-all', {}); }

  workload() { return this.get<Workload>('/api/dashboard/workload'); }
  exposure() { return this.get<Exposure>('/api/dashboard/exposure'); }

  private get<T>(url: string) { return firstValueFrom(this.http.get<T>(url)); }
  private post<T>(url: string, body: unknown) { return firstValueFrom(this.http.post<T>(url, body)); }
}

/** Pulls the human-readable message out of a ProblemDetails error. */
export function errorMessage(err: unknown): string {
  if (err instanceof HttpErrorResponse) {
    const p = err.error as Problem | null;
    if (p?.detail) return p.detail;
    if (p?.title) return p.title;
    // Status 0, or a bare 5xx with no ProblemDetails body, means the dev proxy couldn't reach the API at all.
    if (err.status === 0 || err.status >= 500) return 'Cannot reach the API. Make sure it is running on port 5177.';
    return `Request failed (${err.status}).`;
  }
  return 'Something went wrong.';
}

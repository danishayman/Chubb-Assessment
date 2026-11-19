import { DatePipe } from '@angular/common';
import { Component, effect, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService, errorMessage } from '../../core/api.service';
import { ClaimSummary } from '../../core/models';
import { NotificationsService } from '../../core/notifications.service';
import { money } from '../../shared/format';
import { Skeleton } from '../../shared/skeleton';
import { StatusBadge } from '../../shared/status-badge';

@Component({
  selector: 'app-my-claims',
  imports: [RouterLink, DatePipe, StatusBadge, Skeleton],
  template: `
    <div class="page-head">
      <h1>My claims</h1>
      <a class="btn primary" routerLink="/my-claims/new">Report an incident</a>
    </div>

    @if (error()) { <div class="error">{{ error() }}</div> }

    @if (loading()) {
      @for (i of [1, 2, 3]; track i) { <div class="card"><app-skeleton [rows]="2" /></div> }
    }
    @for (c of claims(); track c.id) {
      <a class="card claim-card" [routerLink]="['/my-claims', c.id]">
        <div class="claim-card-main">
          <div>
            <div class="claim-no">{{ c.claimNumber }}</div>
            <div class="muted small">{{ c.type }} · incident {{ c.incidentDate | date: 'd MMM y' }} · claimed {{ money(c.claimedAmount, c.currency) }}</div>
          </div>
          <app-status [status]="c.status" />
        </div>
        @if (c.openInfoRequests > 0) {
          <div class="action-needed">We need more information from you. Open the claim to reply.</div>
        }
      </a>
    } @empty {
      @if (!loading()) {
        <div class="card empty">
          <h2>No claims yet</h2>
          <p class="muted">If something has happened to your car or home, report it here and track it through to payment.</p>
          <a class="btn primary" routerLink="/my-claims/new">Report an incident</a>
        </div>
      }
    }
  `,
})
export class MyClaims {
  private api = inject(ApiService);
  private notifications = inject(NotificationsService);
  protected claims = signal<ClaimSummary[]>([]);
  protected loading = signal(true);
  protected error = signal('');
  protected money = money;

  constructor() {
    effect(() => {
      this.notifications.changed();
      this.load();
    });
  }

  private async load(): Promise<void> {
    try {
      const list = await this.api.claims();
      this.claims.set([...list].sort((a, b) => b.submittedAt.localeCompare(a.submittedAt)));
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.loading.set(false);
    }
  }
}

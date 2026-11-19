import { DatePipe } from '@angular/common';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService, errorMessage } from '../../core/api.service';
import { ClaimDetail } from '../../core/models';
import { NotificationsService } from '../../core/notifications.service';
import { money } from '../../shared/format';
import { StatusBadge } from '../../shared/status-badge';
import { Timeline } from '../../shared/timeline';

const STEPS = ['Submitted', 'Under review', 'Decision', 'Paid'];

@Component({
  selector: 'app-claim-view',
  imports: [RouterLink, DatePipe, FormsModule, StatusBadge, Timeline],
  template: `
    <a class="back" routerLink="/my-claims">← My claims</a>
    @if (error()) { <div class="error">{{ error() }}</div> }

    @if (claim(); as c) {
      <div class="page-head">
        <div>
          <h1>{{ c.summary.claimNumber }}</h1>
          <div class="muted">{{ c.summary.type }} claim on policy {{ c.policyNumber }}</div>
        </div>
        <app-status [status]="c.summary.status" />
      </div>

      <ol class="steps">
        @for (s of steps; track s; let i = $index) {
          <li [class.done]="i < step()" [class.current]="i === step()" [class.bad]="i === 2 && c.summary.status === 'Rejected'">{{ i === 2 && c.summary.status === 'Rejected' ? 'Not approved' : s }}</li>
        }
      </ol>

      @for (r of openRequests(); track r.id) {
        <div class="card attention stack">
          <strong>We need more information</strong>
          <p>{{ r.question }}</p>
          <textarea rows="3" [(ngModel)]="answers[r.id]" placeholder="Your response"></textarea>
          <div class="actions">
            <button class="btn primary" [disabled]="!answers[r.id]?.trim() || busy()" (click)="respond(r.id)">Send response</button>
          </div>
        </div>
      }

      @if (c.summary.status === 'Approved' || c.summary.status === 'Settled') {
        <div class="card good">
          <strong>Your claim was approved for {{ money(c.summary.approvedAmount, c.summary.currency) }}.</strong>
          @if (c.summary.status === 'Settled') { <div>Paid {{ money(c.settledAmount, c.summary.currency) }} on {{ c.closedAt | date: 'd MMM y' }}.</div> }
          @else { <div>Payment is being arranged.</div> }
          @if (c.decisionReason) { <div class="muted">{{ c.decisionReason }}</div> }
        </div>
      }
      @if (c.summary.status === 'Rejected') {
        <div class="card bad-card">
          <strong>Your claim was not approved.</strong>
          <div>{{ c.decisionReason }}</div>
        </div>
      }

      <div class="grid-2">
        <section class="card">
          <h2>Details</h2>
          <dl class="dl">
            <dt>Incident date</dt><dd>{{ c.summary.incidentDate | date: 'd MMM y' }}</dd>
            <dt>Location</dt><dd>{{ c.location }}</dd>
            <dt>Claimed</dt><dd>{{ money(c.summary.claimedAmount, c.summary.currency) }}</dd>
            <dt>Policy excess</dt><dd>{{ money(c.excess, c.summary.currency) }}</dd>
            <dt>Submitted</dt><dd>{{ c.summary.submittedAt | date: 'd MMM y, HH:mm' }}</dd>
          </dl>
          <p class="pre">{{ c.description }}</p>

          @if (answered().length) {
            <h3>Information you've provided</h3>
            @for (r of answered(); track r.id) {
              <div class="qa"><div class="muted small">{{ r.question }}</div><div>{{ r.response }}</div></div>
            }
          }
        </section>
        <section class="card">
          <h2>History</h2>
          <app-timeline [items]="c.timeline" />
        </section>
      </div>
    }
  `,
})
export class ClaimView {
  private api = inject(ApiService);
  private notifications = inject(NotificationsService);

  id = input.required<string>();
  protected claim = signal<ClaimDetail | null>(null);
  protected error = signal('');
  protected busy = signal(false);
  protected answers: Record<string, string> = {};
  protected steps = STEPS;
  protected money = money;

  protected openRequests = computed(() => this.claim()?.informationRequests.filter(r => r.status === 'Open') ?? []);
  protected answered = computed(() => this.claim()?.informationRequests.filter(r => r.status === 'Answered') ?? []);
  protected step = computed(() => {
    switch (this.claim()?.summary.status) {
      case 'Submitted': return 0;
      case 'UnderReview': case 'AwaitingInformation': return 1;
      case 'Approved': case 'Rejected': return 2;
      case 'Settled': return 4;
      default: return 0;
    }
  });

  constructor() {
    effect(() => {
      this.notifications.changed();
      this.load(this.id());
    });
  }

  private async load(id: string): Promise<void> {
    try {
      this.claim.set(await this.api.claim(id));
    } catch (e) {
      this.error.set(errorMessage(e));
    }
  }

  protected async respond(requestId: string): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      this.claim.set(await this.api.respond(this.id(), requestId, this.answers[requestId]));
      delete this.answers[requestId];
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}

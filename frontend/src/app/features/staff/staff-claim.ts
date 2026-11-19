import { DatePipe } from '@angular/common';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService, errorMessage } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ClaimDetail, Staff } from '../../core/models';
import { NotificationsService } from '../../core/notifications.service';
import { money } from '../../shared/format';
import { AttachmentList } from '../../shared/attachment-list';
import { Icon } from '../../shared/icon';
import { Skeleton } from '../../shared/skeleton';
import { StatusBadge } from '../../shared/status-badge';
import { Timeline } from '../../shared/timeline';

@Component({
  selector: 'app-staff-claim',
  imports: [RouterLink, DatePipe, FormsModule, StatusBadge, Timeline, AttachmentList, Icon, Skeleton],
  template: `
    <a class="back" routerLink="/queue"><app-icon name="back" [size]="16" /> Work queue</a>
    @if (error()) { <div class="error" role="alert">{{ error() }}</div> }

    @if (claim(); as c) {
      <div class="page-head">
        <div>
          <h1>{{ c.summary.claimNumber }}</h1>
          <div class="muted">{{ c.summary.claimantName }} · {{ c.summary.type }} · {{ c.summary.market }} · policy {{ c.policyNumber }} · {{ c.summary.ageDays }} days old</div>
        </div>
        <app-status [status]="c.summary.status" />
      </div>

      <div class="grid-main">
        <div>
          <section class="card">
            <h2>Incident</h2>
            <dl class="dl">
              <dt>Date</dt><dd>{{ c.summary.incidentDate | date: 'd MMM y' }}</dd>
              <dt>Location</dt><dd>{{ c.location }}</dd>
              <dt>Claimed</dt><dd>{{ money(c.summary.claimedAmount, cur()) }}</dd>
              <dt>Coverage limit</dt><dd>{{ money(c.coverageLimit, cur()) }}</dd>
              <dt>Excess</dt><dd>{{ money(c.excess, cur()) }}</dd>
              <dt>Max payable</dt><dd>{{ money(c.maxPayable, cur()) }}</dd>
              <dt>Reserve</dt><dd><strong>{{ money(c.summary.reserveAmount, cur()) }}</strong></dd>
              @if (c.summary.approvedAmount !== null) { <dt>Approved</dt><dd>{{ money(c.summary.approvedAmount, cur()) }}</dd> }
              @if (c.settledAmount !== null) { <dt>Settled</dt><dd>{{ money(c.settledAmount, cur()) }}</dd> }
              @if (c.decisionReason) { <dt>Decision note</dt><dd>{{ c.decisionReason }}</dd> }
            </dl>
            <p class="pre">{{ c.description }}</p>
          </section>

          @if (c.informationRequests.length) {
            <section class="card">
              <h2>Information requests</h2>
              @for (r of c.informationRequests; track r.id) {
                <div class="qa">
                  <div><strong>Q:</strong> {{ r.question }} <span class="muted small">· {{ r.requestedAt | date: 'd MMM, HH:mm' }}</span></div>
                  @if (r.response) { <div><strong>A:</strong> {{ r.response }} <span class="muted small">· {{ r.respondedAt | date: 'd MMM, HH:mm' }}</span></div> }
                  @else { <div class="muted">Waiting for the claimant…</div> }
                  @if (docsFor(r.id).length) {
                    <div class="qa-files"><app-icon name="paperclip" [size]="14" /> {{ docsFor(r.id).length }} document{{ docsFor(r.id).length > 1 ? 's' : '' }} attached</div>
                  }
                </div>
              }
            </section>
          }

          <section class="card">
            <h2>Documents <span class="count">{{ c.attachments.length }}</span></h2>
            <app-attachment-list [claimId]="c.summary.id" [items]="c.attachments" emptyText="The claimant hasn't uploaded any documents." />
          </section>

          <section class="card">
            <h2>Internal notes</h2>
            <div class="stack">
              <textarea rows="2" placeholder="Add a note (not visible to the claimant)" [(ngModel)]="note"></textarea>
              <div class="actions"><button class="btn small" [disabled]="!note.trim() || busy()" (click)="run(api.addNote(id(), note), 'note')">Add note</button></div>
            </div>
            @for (n of c.notes ?? []; track n.id) {
              <div class="note"><div class="muted small">{{ n.authorName }} · {{ n.createdAt | date: 'd MMM, HH:mm' }}</div>{{ n.text }}</div>
            }
          </section>

          <section class="card">
            <h2>Activity</h2>
            <app-timeline [items]="c.timeline" />
          </section>
        </div>

        <aside class="card panel">
          <h2>Actions</h2>

          @if (!isOpen()) {
            <p class="muted">This claim is closed.</p>
          } @else if (!c.summary.assignedOfficerId) {
            <p>Nobody is working on this claim yet.</p>
            <button class="btn primary" [disabled]="busy()" (click)="run(api.assign(id()))">Assign to me</button>
          } @else if (!mine()) {
            <p>Assigned to <strong>{{ c.summary.assignedOfficerName }}</strong>.</p>
          }

          @if (auth.isManager() && isOpen()) {
            <div class="action stack">
              <label>Assign to
                <select [(ngModel)]="assignTo">
                  @for (s of staff(); track s.id) { <option [value]="s.id">{{ s.name }}</option> }
                </select>
              </label>
              <button class="btn small" [disabled]="!assignTo || assignTo === c.summary.assignedOfficerId || busy()" (click)="run(api.assign(id(), assignTo))">Reassign</button>
            </div>
          }

          @if (mine() && isOpen()) {
            @if (c.summary.status === 'Approved') {
              <p>Approved for {{ money(c.summary.approvedAmount, cur()) }}. Release payment to close the claim.</p>
              <button class="btn good" [disabled]="busy()" (click)="run(api.settle(id()))">Settle claim</button>
            } @else {
              @if (c.summary.status === 'AwaitingInformation') {
                <p class="hint">Waiting for the claimant to respond. You can't approve until every request is answered.</p>
              }

              <div class="action stack">
                <label>Reserve ({{ cur() }})
                  <div class="money-row">
                    <input type="number" min="0" step="0.01" [(ngModel)]="reserve" />
                    <button class="btn small" [disabled]="busy() || reserve === c.summary.reserveAmount" (click)="run(api.setReserve(id(), reserve))">Update</button>
                  </div>
                </label>
              </div>

              <div class="action stack">
                <label>Request information
                  <textarea rows="2" [(ngModel)]="question" placeholder="What do you need from the claimant?"></textarea>
                </label>
                <button class="btn small" [disabled]="!question.trim() || busy()" (click)="run(api.requestInfo(id(), question), 'question')">Send request</button>
              </div>

              @if (c.summary.status === 'UnderReview') {
                <div class="action stack">
                  <label>Approve amount ({{ cur() }}, max {{ money(c.maxPayable, cur()) }})
                    <input type="number" min="0" step="0.01" [(ngModel)]="approveAmount" />
                  </label>
                  <input placeholder="Reason (optional)" [(ngModel)]="approveReason" />
                  <button class="btn good" [disabled]="!(approveAmount > 0) || busy()" (click)="run(api.approve(id(), approveAmount, approveReason))">Approve</button>
                </div>
              }

              <div class="action stack">
                <label>Reject
                  <input placeholder="Reason, shown to the claimant" [(ngModel)]="rejectReason" [disabled]="confirmingReject()" />
                </label>
                @if (!confirmingReject()) {
                  <button class="btn danger" [disabled]="!rejectReason.trim() || busy()" (click)="confirmingReject.set(true)">Reject claim</button>
                } @else {
                  <p class="small">This closes the claim and notifies the claimant. It can't be undone.</p>
                  <div class="actions">
                    <button class="btn ghost small" [disabled]="busy()" (click)="confirmingReject.set(false)">Keep open</button>
                    <button class="btn danger-solid small" [disabled]="busy()" (click)="reject()">Yes, reject claim</button>
                  </div>
                }
              </div>
            }
          }
        </aside>
      </div>
    } @else if (!error()) {
      <div class="card"><app-skeleton [rows]="2" [height]="22" /></div>
      <div class="grid-main"><div class="card"><app-skeleton [rows]="8" /></div><div class="card"><app-skeleton [rows]="5" /></div></div>
    }
  `,
})
export class StaffClaim {
  protected api = inject(ApiService);
  protected auth = inject(AuthService);
  private notifications = inject(NotificationsService);

  id = input.required<string>();
  protected claim = signal<ClaimDetail | null>(null);
  protected staff = signal<Staff[]>([]);
  protected error = signal('');
  protected busy = signal(false);
  protected money = money;

  protected note = '';
  protected question = '';
  protected reserve = 0;
  protected approveAmount = 0;
  protected approveReason = '';
  protected rejectReason = '';
  protected assignTo = '';

  protected confirmingReject = signal(false);

  protected cur = computed(() => this.claim()?.summary.currency ?? '');
  protected mine = computed(() => this.claim()?.summary.assignedOfficerId === this.auth.user()?.id);
  protected isOpen = computed(() => {
    const s = this.claim()?.summary.status;
    return s !== 'Settled' && s !== 'Rejected';
  });

  constructor() {
    effect(() => {
      this.notifications.changed();
      this.load(this.id());
    });
    if (this.auth.isManager()) this.api.staff().then(s => this.staff.set(s));
  }

  private async load(id: string): Promise<void> {
    try {
      this.set(await this.api.claim(id));
    } catch (e) {
      this.error.set(errorMessage(e));
    }
  }

  private set(c: ClaimDetail): void {
    this.claim.set(c);
    this.reserve = c.summary.reserveAmount ?? 0;
    this.approveAmount = Math.min(c.summary.reserveAmount ?? 0, c.maxPayable ?? 0);
    this.assignTo = c.summary.assignedOfficerId ?? '';
  }

  protected async run(action: Promise<ClaimDetail>, clear?: 'note' | 'question'): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      this.set(await action);
      if (clear) this[clear] = '';
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected docsFor(requestId: string) {
    return this.claim()?.attachments.filter(a => a.informationRequestId === requestId) ?? [];
  }

  protected async reject(): Promise<void> {
    await this.run(this.api.reject(this.id(), this.rejectReason));
    this.confirmingReject.set(false);
  }
}

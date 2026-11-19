import { DatePipe } from '@angular/common';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService, errorMessage } from '../../core/api.service';
import { ClaimDetail } from '../../core/models';
import { NotificationsService } from '../../core/notifications.service';
import { AttachmentList } from '../../shared/attachment-list';
import { FilePicker } from '../../shared/file-picker';
import { money } from '../../shared/format';
import { Icon } from '../../shared/icon';
import { Skeleton } from '../../shared/skeleton';
import { StatusBadge } from '../../shared/status-badge';
import { Timeline } from '../../shared/timeline';

const STEPS = ['Submitted', 'Under review', 'Decision', 'Paid'];

@Component({
  selector: 'app-claim-view',
  imports: [RouterLink, DatePipe, FormsModule, StatusBadge, Timeline, AttachmentList, FilePicker, Icon, Skeleton],
  template: `
    <a class="back" routerLink="/my-claims"><app-icon name="back" [size]="16" /> My claims</a>
    @if (error()) { <div class="error" role="alert">{{ error() }}</div> }
    @if (uploadFailed().length) {
      <div class="warning" role="status">
        <app-icon name="alert" [size]="16" />
        <span>Your claim was submitted, but {{ uploadFailed().join(', ') }} didn't upload. Add {{ uploadFailed().length > 1 ? 'them' : 'it' }} again below.</span>
      </div>
    }

    @if (claim(); as c) {
      <div class="page-head">
        <div>
          <h1>{{ c.summary.claimNumber }}</h1>
          <div class="muted">{{ c.summary.type }} claim on policy {{ c.policyNumber }}</div>
        </div>
        <app-status [status]="c.summary.status" />
      </div>

      <ol class="steps" aria-label="Claim progress">
        @for (s of steps; track s; let i = $index) {
          <li [class.done]="i < step()" [class.current]="i === step()" [class.bad]="i === 2 && c.summary.status === 'Rejected'"
              [attr.aria-current]="i === step() ? 'step' : null">
            {{ i === 2 && c.summary.status === 'Rejected' ? 'Not approved' : s }}
          </li>
        }
      </ol>

      @for (r of openRequests(); track r.id) {
        <section class="card attention stack">
          <div>
            <h2>We need more information</h2>
            <p class="question">{{ r.question }}</p>
          </div>
          <label>Your reply
            <textarea rows="3" [(ngModel)]="answers[r.id]" placeholder="Answer here, or just attach the documents we asked for"></textarea>
          </label>
          <app-file-picker [files]="filesFor(r.id)" (filesChange)="setFiles(r.id, $event)" [disabled]="busy()" [compact]="true" />
          <div class="actions">
            <button class="btn primary" [disabled]="!canSend(r.id) || busy()" (click)="respond(r.id)">
              {{ busy() ? progress() || 'Sending…' : 'Send reply' }}
            </button>
          </div>
        </section>
      }

      @if (c.summary.status === 'Approved' || c.summary.status === 'Settled') {
        <section class="card good">
          <h2>Approved for {{ money(c.summary.approvedAmount, c.summary.currency) }}</h2>
          @if (c.summary.status === 'Settled') { <p>Paid {{ money(c.settledAmount, c.summary.currency) }} on {{ c.closedAt | date: 'd MMM y' }}.</p> }
          @else { <p>We're arranging payment and will let you know when it's sent.</p> }
          @if (c.decisionReason) { <p class="muted">{{ c.decisionReason }}</p> }
        </section>
      }
      @if (c.summary.status === 'Rejected') {
        <section class="card bad-card">
          <h2>Your claim was not approved</h2>
          <p>{{ c.decisionReason }}</p>
        </section>
      }

      <div class="grid-2">
        <div>
          <section class="card">
            <h2>Details</h2>
            <dl class="dl">
              <dt>Incident date</dt><dd>{{ c.summary.incidentDate | date: 'd MMM y' }}</dd>
              <dt>Location</dt><dd>{{ c.location }}</dd>
              <dt>Claimed</dt><dd class="num">{{ money(c.summary.claimedAmount, c.summary.currency) }}</dd>
              <dt>Policy excess</dt><dd class="num">{{ money(c.excess, c.summary.currency) }}</dd>
              <dt>Submitted</dt><dd>{{ c.summary.submittedAt | date: 'd MMM y, HH:mm' }}</dd>
            </dl>
            <p class="pre">{{ c.description }}</p>

            @if (answered().length) {
              <h3>Your replies</h3>
              @for (r of answered(); track r.id) {
                <div class="qa"><div class="muted small">{{ r.question }}</div><div>{{ r.response }}</div></div>
              }
            }
          </section>

          <section class="card">
            <div class="section-head">
              <h2>Documents</h2>
              @if (isOpen() && !addingDocs()) {
                <button class="btn small ghost" (click)="addingDocs.set(true)"><app-icon name="plus" [size]="15" /> Add documents</button>
              }
            </div>
            <app-attachment-list [claimId]="c.summary.id" [items]="c.attachments" emptyText="No documents yet." />
            @if (addingDocs()) {
              <div class="stack add-docs">
                <app-file-picker [(files)]="extraFiles" [disabled]="busy()" [compact]="true" />
                <div class="actions">
                  <button class="btn ghost small" [disabled]="busy()" (click)="addingDocs.set(false); extraFiles.set([])">Cancel</button>
                  <button class="btn primary small" [disabled]="!extraFiles().length || busy()" (click)="uploadExtra()">
                    {{ busy() ? progress() || 'Uploading…' : 'Upload' }}
                  </button>
                </div>
              </div>
            }
          </section>
        </div>

        <section class="card">
          <h2>History</h2>
          <app-timeline [items]="c.timeline" />
        </section>
      </div>
    } @else if (!error()) {
      <div class="card"><app-skeleton [rows]="2" [height]="22" /></div>
      <div class="grid-2"><div class="card"><app-skeleton [rows]="5" /></div><div class="card"><app-skeleton [rows]="5" /></div></div>
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
  protected progress = signal('');
  protected addingDocs = signal(false);
  protected extraFiles = signal<File[]>([]);
  protected uploadFailed = signal<string[]>((history.state?.uploadFailed as string[] | undefined) ?? []);
  protected answers: Record<string, string> = {};
  private replyFiles = signal<Record<string, File[]>>({});
  protected steps = STEPS;
  protected money = money;

  protected openRequests = computed(() => this.claim()?.informationRequests.filter(r => r.status === 'Open') ?? []);
  protected answered = computed(() => this.claim()?.informationRequests.filter(r => r.status === 'Answered') ?? []);
  protected isOpen = computed(() => {
    const s = this.claim()?.summary.status;
    return !!s && s !== 'Settled' && s !== 'Rejected';
  });
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

  protected filesFor(requestId: string): File[] { return this.replyFiles()[requestId] ?? []; }
  protected setFiles(requestId: string, files: File[]): void { this.replyFiles.update(m => ({ ...m, [requestId]: files })); }
  protected canSend(requestId: string): boolean { return !!this.answers[requestId]?.trim() || this.filesFor(requestId).length > 0; }

  private async load(id: string): Promise<void> {
    try {
      this.claim.set(await this.api.claim(id));
    } catch (e) {
      this.error.set(errorMessage(e));
    }
  }

  /** Upload the files first, linked to the request, so the officer sees them the moment the claim returns to review. */
  protected async respond(requestId: string): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      const files = this.filesFor(requestId);
      await this.uploadAll(files, requestId, f => this.setFiles(requestId, this.filesFor(requestId).filter(x => x !== f)));
      const text = this.answers[requestId]?.trim()
        || `I've attached ${files.length} document${files.length > 1 ? 's' : ''}: ${files.map(f => f.name).join(', ')}.`;
      this.claim.set(await this.api.respond(this.id(), requestId, text));
      delete this.answers[requestId];
      this.setFiles(requestId, []);
    } catch (e) {
      this.error.set(errorMessage(e));
      await this.load(this.id());
    } finally {
      this.busy.set(false);
      this.progress.set('');
    }
  }

  protected async uploadExtra(): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      await this.uploadAll(this.extraFiles(), undefined, f => this.extraFiles.update(l => l.filter(x => x !== f)));
      this.extraFiles.set([]);
      this.addingDocs.set(false);
      this.uploadFailed.set([]);
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      await this.load(this.id());
      this.busy.set(false);
      this.progress.set('');
    }
  }

  /** Uploads one at a time. Each file leaves the picker once it's stored, so a retry after a failure doesn't duplicate it. */
  private async uploadAll(files: File[], requestId: string | undefined, uploaded: (f: File) => void): Promise<void> {
    for (const [i, f] of files.entries()) {
      this.progress.set(`Uploading ${i + 1} of ${files.length}…`);
      await this.api.uploadAttachment(this.id(), f, requestId);
      uploaded(f);
    }
  }
}

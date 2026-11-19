import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiService, errorMessage } from '../../core/api.service';
import { Policy } from '../../core/models';
import { FilePicker } from '../../shared/file-picker';
import { money } from '../../shared/format';
import { Icon } from '../../shared/icon';

@Component({
  selector: 'app-new-claim',
  imports: [ReactiveFormsModule, RouterLink, FilePicker, Icon],
  template: `
    <a class="back" routerLink="/my-claims"><app-icon name="back" [size]="16" /> My claims</a>
    <div class="page-head">
      <div>
        <h1>Report an incident</h1>
        <p class="muted lede">Tell us what happened. You can add more detail and documents later if we ask.</p>
      </div>
    </div>

    <div class="form-layout">
      <form class="card stack" [formGroup]="form" (ngSubmit)="submit()">
        <section class="form-section">
          <h2>Policy</h2>
          <label>Which policy is this claim under?
            <select formControlName="policyId">
              <option value="" disabled>Select a policy</option>
              @for (p of policies(); track p.id) {
                <option [value]="p.id">{{ p.policyNumber }} · {{ p.type }} ({{ p.market }})</option>
              }
            </select>
          </label>
          @if (selected(); as p) {
            <div class="hint">
              Covers up to <strong>{{ money(p.coverageLimit, p.currency) }}</strong>.
              The first <strong>{{ money(p.excess, p.currency) }}</strong> of any claim is your excess.
            </div>
          }
        </section>

        <section class="form-section">
          <h2>What happened</h2>
          <div class="row">
            <label>Date of incident <input type="date" formControlName="incidentDate" [max]="today" /></label>
            <label>Estimated loss{{ selected() ? ' (' + selected()!.currency + ')' : '' }}
              <input type="number" min="1" step="0.01" inputmode="decimal" formControlName="claimedAmount" placeholder="0.00" />
            </label>
          </div>
          <label>Where did it happen? <input formControlName="location" maxlength="300" placeholder="Street and city" /></label>
          <label>Describe the incident
            <textarea formControlName="description" rows="5" maxlength="4000"
                      placeholder="What happened, what was damaged or lost, and anyone else involved"></textarea>
            <span class="muted small counter">{{ descriptionLength() }} / 4000</span>
          </label>
        </section>

        <section class="form-section">
          <h2>Supporting documents <span class="optional">Optional</span></h2>
          <p class="muted small">Photos of the damage, a police report, receipts or repair quotes help us decide faster.</p>
          <app-file-picker [(files)]="files" [disabled]="busy()" />
        </section>

        @if (error()) { <div class="error" role="alert">{{ error() }}</div> }
        <div class="actions">
          <a class="btn ghost" routerLink="/my-claims">Cancel</a>
          <button class="btn primary" type="submit" [disabled]="form.invalid || busy()">{{ busyLabel() }}</button>
        </div>
      </form>

      <aside class="aside">
        <h2>What happens next</h2>
        <ol class="next-steps">
          <li><strong>We confirm receipt</strong><span>You'll get a notification with your claim number straight away.</span></li>
          <li><strong>An officer reviews it</strong><span>They may ask you for more information or documents.</span></li>
          <li><strong>You get a decision</strong><span>If it's approved, we arrange payment and let you know when it's sent.</span></li>
        </ol>
      </aside>
    </div>
  `,
})
export class NewClaim {
  private api = inject(ApiService);
  private router = inject(Router);
  private fb = inject(FormBuilder);

  protected today = new Date().toISOString().slice(0, 10);
  protected policies = signal<Policy[]>([]);
  protected files = signal<File[]>([]);
  protected busy = signal(false);
  protected progress = signal('');
  protected error = signal('');
  protected money = money;

  protected form = this.fb.nonNullable.group({
    policyId: ['', Validators.required],
    incidentDate: [this.today, Validators.required],
    claimedAmount: [null as number | null, [Validators.required, Validators.min(1)]],
    location: ['', [Validators.required, Validators.maxLength(300)]],
    description: ['', [Validators.required, Validators.maxLength(4000)]],
  });

  private policyId = signal('');
  protected descriptionLength = signal(0);
  protected selected = computed(() => this.policies().find(p => p.id === this.policyId()) ?? null);
  protected busyLabel = computed(() => (this.busy() ? this.progress() || 'Submitting…' : 'Submit claim'));

  constructor() {
    this.form.controls.policyId.valueChanges.subscribe(v => this.policyId.set(v));
    this.form.controls.description.valueChanges.subscribe(v => this.descriptionLength.set(v.length));
    this.api.myPolicies().then(p => {
      this.policies.set(p);
      if (p.length === 1) this.form.patchValue({ policyId: p[0].id });
    }).catch(e => this.error.set(errorMessage(e)));
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid) return;
    this.busy.set(true);
    this.error.set('');
    try {
      const v = this.form.getRawValue();
      const claim = await this.api.submit({ ...v, claimedAmount: v.claimedAmount ?? 0 });

      // The claim exists now, so a failed upload must not block or resubmit it.
      // Carry on to the claim page and tell the claimant which files to try again.
      const failed: string[] = [];
      const files = this.files();
      for (const [i, f] of files.entries()) {
        this.progress.set(`Uploading ${i + 1} of ${files.length}…`);
        try { await this.api.uploadAttachment(claim.summary.id, f); } catch { failed.push(f.name); }
      }
      this.router.navigate(['/my-claims', claim.summary.id], { state: { uploadFailed: failed } });
    } catch (e) {
      this.error.set(errorMessage(e));
      this.busy.set(false);
    }
  }
}

import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiService, errorMessage } from '../../core/api.service';
import { Policy } from '../../core/models';
import { money } from '../../shared/format';

@Component({
  selector: 'app-new-claim',
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    <div class="page-head"><h1>Report an incident</h1></div>

    <form class="card stack form-narrow" [formGroup]="form" (ngSubmit)="submit()">
      <label>Policy
        <select formControlName="policyId">
          <option value="" disabled>Select a policy</option>
          @for (p of policies(); track p.id) {
            <option [value]="p.id">{{ p.policyNumber }} · {{ p.type }} ({{ p.market }})</option>
          }
        </select>
      </label>
      @if (selected(); as p) {
        <div class="hint">Cover up to {{ money(p.coverageLimit, p.currency) }}. Excess {{ money(p.excess, p.currency) }}. Valid {{ p.startDate }} to {{ p.endDate }}.</div>
      }

      <div class="row">
        <label>Date of incident <input type="date" formControlName="incidentDate" [max]="today" /></label>
        <label>Estimated loss {{ selected() ? '(' + selected()!.currency + ')' : '' }}
          <input type="number" min="1" step="0.01" formControlName="claimedAmount" />
        </label>
      </div>
      <label>Where did it happen? <input formControlName="location" maxlength="300" placeholder="Street, city" /></label>
      <label>What happened?
        <textarea formControlName="description" rows="5" maxlength="4000" placeholder="Describe the incident and the damage"></textarea>
      </label>

      @if (error()) { <div class="error">{{ error() }}</div> }
      <div class="actions">
        <a class="btn ghost" routerLink="/my-claims">Cancel</a>
        <button class="btn primary" type="submit" [disabled]="form.invalid || busy()">{{ busy() ? 'Submitting…' : 'Submit claim' }}</button>
      </div>
    </form>
  `,
})
export class NewClaim {
  private api = inject(ApiService);
  private router = inject(Router);
  private fb = inject(FormBuilder);

  protected today = new Date().toISOString().slice(0, 10);
  protected policies = signal<Policy[]>([]);
  protected busy = signal(false);
  protected error = signal('');
  protected money = money;

  protected form = this.fb.nonNullable.group({
    policyId: ['', Validators.required],
    incidentDate: [this.today, Validators.required],
    claimedAmount: [0, [Validators.required, Validators.min(1)]],
    location: ['', [Validators.required, Validators.maxLength(300)]],
    description: ['', [Validators.required, Validators.maxLength(4000)]],
  });

  private policyId = signal('');
  protected selected = computed(() => this.policies().find(p => p.id === this.policyId()) ?? null);

  constructor() {
    this.form.controls.policyId.valueChanges.subscribe(v => this.policyId.set(v));
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
      const claim = await this.api.submit(this.form.getRawValue());
      this.router.navigate(['/my-claims', claim.summary.id]);
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}

import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

export const claimantGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  if (!auth.user()) return inject(Router).parseUrl('/login');
  return auth.isStaff() ? inject(Router).parseUrl('/queue') : true;
};

export const staffGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  if (!auth.user()) return inject(Router).parseUrl('/login');
  return auth.isStaff() ? true : inject(Router).parseUrl('/my-claims');
};

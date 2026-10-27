import { Routes } from '@angular/router';
import { roleGuard } from '../../core/auth/guards';

export default [
  { path: 'warehouses', loadComponent: () => import('./masters').then(m => m.Masters), data: { kind: 'warehouses' } },
  { path: 'suppliers', canActivate: [roleGuard('Administrator', 'Purchasing', 'Approver')], loadComponent: () => import('./masters').then(m => m.Masters), data: { kind: 'suppliers' } },
  { path: 'purchase-orders', canActivate: [roleGuard('Administrator', 'Purchasing', 'Approver')], children: [
    { path: '', data: { kind: 'purchase' }, loadComponent: () => import('./document-list').then(m => m.DocumentList) },
    { path: 'new', canActivate: [roleGuard('Administrator', 'Purchasing')], loadComponent: () => import('./purchase-form').then(m => m.PurchaseForm) },
    { path: ':id/edit', canActivate: [roleGuard('Administrator', 'Purchasing')], loadComponent: () => import('./purchase-form').then(m => m.PurchaseForm) },
    { path: ':id', loadComponent: () => import('./purchase-detail').then(m => m.PurchaseDetail) },
  ] },
  { path: 'receiving', canActivate: [roleGuard('Administrator', 'Purchasing', 'Approver', 'WarehouseOperator')], children: [
    { path: '', data: { kind: 'receiving' }, loadComponent: () => import('./document-list').then(m => m.DocumentList) },
    { path: ':id', loadComponent: () => import('./receive-order').then(m => m.ReceiveOrder) },
  ] },
  { path: 'goods-receipts', canActivate: [roleGuard('Administrator', 'WarehouseOperator')], children: [
    { path: '', data: { kind: 'receipt' }, loadComponent: () => import('./document-list').then(m => m.DocumentList) },
    { path: ':id', data: { kind: 'receipt' }, loadComponent: () => import('./stock-document').then(m => m.StockDocument) },
  ] },
  { path: 'stock-transfers', canActivate: [roleGuard('Administrator', 'WarehouseOperator')], children: [
    { path: '', data: { kind: 'transfer' }, loadComponent: () => import('./document-list').then(m => m.DocumentList) },
    { path: 'new', data: { transfer: true }, loadComponent: () => import('./purchase-form').then(m => m.PurchaseForm) },
    { path: ':id', data: { kind: 'transfer' }, loadComponent: () => import('./stock-document').then(m => m.StockDocument) },
  ] },
  { path: 'inventory', canActivate: [roleGuard('Administrator', 'Purchasing', 'Approver', 'WarehouseOperator')], loadComponent: () => import('./inventory').then(m => m.Inventory) },
] satisfies Routes;

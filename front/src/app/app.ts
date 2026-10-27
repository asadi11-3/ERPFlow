import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { PwaStatus } from './layout/pwa-status/pwa-status';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, PwaStatus],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {}

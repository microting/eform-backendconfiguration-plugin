import {Observable} from 'rxjs';

/**
 * The load state of a Kemi screen (W1, W3, W5): only the newest request may deliver (another property,
 * or a reload after a save), and the screen is loading until it answers. A refused answer, an error and
 * a request that completes without any answer (the host interceptor ends a 5xx or a network failure with
 * EMPTY) all count as failed, so a screen is never stuck on "loading". The host toasts a refusal itself.
 */
export class ChemicalLatestLoad {
  loading = false;
  failed = false;
  private generation = 0;

  /** Drops the request in flight (its answer is ignored) and shows neither loading nor failed. */
  reset(): void {
    this.generation++;
    this.loading = false;
    this.failed = false;
  }

  run<T>(request: Observable<{success: boolean; model: T} | null>, loaded: (model: T) => void): void {
    const generation = ++this.generation;
    const current = () => generation === this.generation;
    let answered = false;
    this.loading = true;
    this.failed = false;
    request.subscribe({
      next: (res) => {
        if (!current()) {
          return;
        }
        answered = true;
        this.loading = false;
        if (res && res.success) {
          loaded(res.model);
        } else {
          this.failed = true;
        }
      },
      error: () => this.fail(current()),
      complete: () => this.fail(current() && !answered),
    });
  }

  private fail(applies: boolean): void {
    if (applies) {
      this.loading = false;
      this.failed = true;
    }
  }
}

/**
 * Direct SQL against the CI MariaDB container, for the few specs that must put
 * the database into a state no API can produce.
 *
 * Depends on .github/workflows/dotnet-core-pr.yml, job pn-playwright-test: its
 * "Start MariaDB" step (`docker run --name mariadbtest ...`) starts the database
 * on the same runner host the specs run on, with the root password it passes as
 * MYSQL_ROOT_PASSWORD; the job's own "Change rabbitmq hostname" step already runs
 * `docker exec -i mariadbtest mariadb -u root ...` the same way. Tests run in CI
 * only (CLAUDE.md), so there is no local fallback.
 *
 * The password travels as MYSQL_PWD — `docker exec -e MYSQL_PWD` forwards it from
 * this process's environment into the container — never on a command line, so it
 * stays out of the process list and mariadb's "password on the command line" warning.
 */
import { execFile } from 'child_process';
import { promisify } from 'util';
import DatabaseConfigurationConstants from '../../Constants/DatabaseConfigurationConstants';
import { API_TIMEOUT } from './wait-helpers';

const execFileAsync = promisify(execFile);

/**
 * The MariaDB root password, taken from the connection credentials the
 * database-configuration step (DatabaseConfigurationConstants.authenticationType)
 * sets the app up with — the same root account the workflow starts the container
 * with — rather than repeated here.
 */
function mariadbRootPassword(): string {
  const match = /password\s*=\s*([^;]+);/.exec(DatabaseConfigurationConstants.authenticationType);
  if (!match) {
    throw new Error('DatabaseConfigurationConstants.authenticationType carries no "password = ...;" part');
  }
  return match[1].trim();
}

/** A schema name under the customer number the database-configuration step sets up, e.g. `420_SDK`. */
export function customerDatabase(suffix: string): string {
  return `${DatabaseConfigurationConstants.customerNo}_${suffix}`;
}

/**
 * Runs `sql` in the CI MariaDB container (`-N -B`: no column headers, tab-separated
 * rows) and returns stdout. `description` completes the sentence "Could not … via
 * docker exec", so a failure names what the spec was trying to do.
 *
 * Callers interpolate only values they have validated as integers — this is a test
 * helper, not a query builder.
 */
export async function runMariadbSql(sql: string, description: string, database?: string): Promise<string> {
  const args = ['exec', '-e', 'MYSQL_PWD', 'mariadbtest', 'mariadb', '-u', 'root', '-N', '-B'];
  if (database) {
    args.push(`--database=${database}`);
  }
  args.push('-e', sql);
  try {
    const { stdout } = await execFileAsync('docker', args, {
      timeout: API_TIMEOUT,
      env: { ...process.env, MYSQL_PWD: mariadbRootPassword() },
    });
    return stdout;
  } catch (error) {
    throw new Error(`Could not ${description} via docker exec: ${String(error)}`);
  }
}

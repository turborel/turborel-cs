# Appendix C: Settings

[Contents](README.md)

Every connection setting, by its name in `ConnectionOptions`, a URI's
query and the service file, and its environment variable, if it has one
([Settings](../client/26-settings.md)), then the settings of a pool.

| Setting | Variable | What it says | Default |
|---|---|---|---|
| `host` | TURBOREL_HOST | The servers, comma-separated: `name`, `name:port`, `[address]:port`, or a Unix socket's or named pipe's path | `localhost` |
| `port` | TURBOREL_PORT | The port of each host that carries none: one for all, or one each | 7422 |
| `user` | TURBOREL_USER | The user to connect as | none: it must be given |
| `password` | TURBOREL_PASSWORD | The user's password | the password file's |
| `tls_mode` | TURBOREL_TLS_MODE | `disable`, `prefer`, `require`, `verify-ca` or `verify-full` | `prefer` |
| `channel_binding` | | `disable`, `prefer` or `require` | `prefer` |
| `application_name` | TURBOREL_APPLICATION_NAME | The program's name, sent as the connection attribute `application` | none |
| `connect_timeout` | TURBOREL_CONNECT_TIMEOUT | Whole seconds each step of connecting waits; 0 for no limit | 30 |
| `service` | TURBOREL_SERVICE | The section of the service file to read | none |
| `service_file` | TURBOREL_SERVICE_FILE | The service file | `~/.turborel_service.conf` |
| `passfile` | TURBOREL_PASSFILE | The password file | `~/.turborel_pass` |
| `target` | | `any`, `read-write` or `read-only` | `any` |
| `load_balance` | | `disable`, the hosts tried in order, or `random` | `disable` |
| `tls_roots` | | A PEM file of the certificate authorities trusted | none |
| `tls_system_roots` | | `true` to trust the platform's authorities too | `false` |
| `tls_certificate`, `tls_key` | | PEM files of the client's certificate chain and key, to present | none |
| `server_name` | | The name the server's certificate is checked against | the host's |

The layers, each setting taken from the first that has it: the
program's options or URI; the service file's section; the environment;
the defaults. A variable set but empty gives nothing.

## A pool's settings

A pool's settings are its `PoolConfig` ([Pools](../client/28-pools.md)).

| `PoolConfig` | What it says | Default |
|---|---|---|
| `Max` | The most connections made at once, handed out and idle | 10 |
| `MaxIdle` | The most kept idle | 10 |
| `IdleTimeout` | How long one may stay idle | 10 minutes |
| `MaxLifetime` | How long one lives from when it was made | 1 hour |
| `Wait` | How long `GetAsync` waits for one to come back | 30 seconds |
| `Check` | Whether an idle connection is checked to answer before it is handed out | yes |
| `Reset` | Statements run as each comes back, after the pool's own | none |

In the standard API's connection string, a pool's settings are keys of
their own, beside the connection's
([TurborelFactory](../client/08-api-factory.md)):

| Key | `PoolConfig` |
|---|---|
| `pooling` | whether there is a pool: `true` or `false` |
| `pool_max` | `Max` |
| `pool_max_idle` | `MaxIdle` |
| `pool_idle_timeout` | `IdleTimeout`, whole seconds, 0 for no limit |
| `pool_max_lifetime` | `MaxLifetime`, whole seconds, 0 for no limit |
| `pool_wait` | `Wait`, whole seconds |
| `pool_check` | `Check`: `true` or `false` |

## STANDARD

[CLIENT Where settings come from] and [CLIENT Pools] of
[Settings, Pipelines and Pools](https://github.com/turborel/turborel/blob/main/doc/client/02-library-connections.md).

## SEE ALSO

[Settings](../user/09-settings.md), [Settings](../client/26-settings.md).

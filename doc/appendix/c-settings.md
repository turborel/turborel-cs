# Appendix C: Settings

[Contents](README.md)

Every connection setting, by its name in `ConnectionOptions`, a URI's
query and the service file, and its environment variable, if it has one
([Settings](../client/26-settings.md)). The pool's settings join this
appendix at milestone M6.

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

## STANDARD

[CLIENT Where settings come from] of
[Settings, Pipelines and Pools](https://github.com/turborel/turborel/blob/main/doc/client/02-library-connections.md).

## SEE ALSO

[Settings](../user/09-settings.md), [Settings](../client/26-settings.md).

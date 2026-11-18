export function resolveApiOrigin(configured: string | undefined, allowLocalDemo: boolean): string {
  const address = configured?.trim() || (allowLocalDemo ? 'http://localhost:5180' : '');
  // Use the same origin syntax on the web and on React Native, including IPv6 loopback.
  const match = /^(https?):\/\/(\[[0-9a-f:.]+\]|[a-z0-9-]+(?:\.[a-z0-9-]+)*)(?::([0-9]{1,5}))?\/?$/i.exec(address);
  if (match) {
    const [, protocol, host, port] = match;
    const scheme = protocol.toLowerCase();
    const hostname = host.toLowerCase();
    const loopback = ['localhost', '127.0.0.1', '[::1]'].includes(hostname);
    if ((!port || (Number(port) > 0 && Number(port) <= 65535))
        && (scheme === 'https' || (allowLocalDemo && loopback))) {
      return `${scheme}://${hostname}${port ? ':' + Number(port) : ''}`;
    }
  }

  throw new Error('Configure uma origem HTTPS para a API. HTTP só é aceito na demonstração local.');
}

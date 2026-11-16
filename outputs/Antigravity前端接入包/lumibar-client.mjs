import net from 'node:net';
import fs from 'node:fs/promises';
import { EventEmitter } from 'node:events';

/** Run in Node/Electron main/preload, not an ordinary browser page. */
export class LumiBarClient extends EventEmitter {
  #socket; #buffer = ''; #pending = new Map(); #sequence = 0;
  async connect(endpointFile) {
    if (this.#socket) throw new Error('Already connected');
    const endpoint = JSON.parse(await fs.readFile(endpointFile, 'utf8'));
    if (endpoint.protocolVersion !== 1 || !/^LumiBar\.Frontend\.v1\.[A-F0-9]+\.\d+(\.Preview)?$/.test(endpoint.pipeName)) throw new Error('Invalid backend endpoint');
    this.#buffer = '';
    const socket = this.#socket = net.createConnection('\\\\.\\pipe\\' + endpoint.pipeName);
    socket.setEncoding('utf8');
    socket.on('data', chunk => this.#receive(chunk));
    socket.on('error', error => this.emit('connectionError', error));
    socket.on('close', () => {
      this.#socket = undefined;
      for (const entry of this.#pending.values()) { clearTimeout(entry.timer); entry.reject(new Error('Backend disconnected')); }
      this.#pending.clear(); this.emit('disconnected');
    });
    await new Promise((resolve, reject) => {
      const timer = setTimeout(() => { socket.destroy(); reject(new Error('Connect timeout')); }, 5000);
      socket.once('connect', () => { clearTimeout(timer); resolve(); });
      socket.once('error', error => { clearTimeout(timer); reject(error); });
    });
    return await this.request('hello', { protocolVersion: 1, clientName: 'antigravity-preview' });
  }
  request(method, params = {}) {
    if (!this.#socket || this.#socket.destroyed) return Promise.reject(new Error('Not connected'));
    const id = String(++this.#sequence), frame = JSON.stringify({ id, method, params }) + '\n';
    if (frame.length > 16384) return Promise.reject(new Error('Request exceeds 16384 characters'));
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => { this.#pending.delete(id); reject(new Error('Request timeout')); }, 45000);
      this.#pending.set(id, { resolve, reject, timer });
      this.#socket.write(frame, error => { if (error) { clearTimeout(timer); this.#pending.delete(id); reject(error); } });
    });
  }
  #receive(chunk) {
    this.#buffer += chunk;
    if (this.#buffer.length > 16 * 1024 * 1024) { this.close(); return; }
    let end;
    while ((end = this.#buffer.indexOf('\n')) >= 0) {
      const line = this.#buffer.slice(0, end); this.#buffer = this.#buffer.slice(end + 1);
      let response; try { response = JSON.parse(line); } catch { this.close(); return; }
      const entry = this.#pending.get(response.id); if (!entry) continue;
      clearTimeout(entry.timer); this.#pending.delete(response.id);
      if (response.ok) entry.resolve(response.data);
      else { const error = new Error(response.error?.message ?? 'Backend error'); error.code = response.error?.code; entry.reject(error); }
    }
  }
  close() { this.#socket?.destroy(); }
}

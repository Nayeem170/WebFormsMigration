import http from 'k6/http';
import { check } from 'k6';

const front = __ENV.FRONT || 'http://localhost:8081';
const catalog = __ENV.CATALOG || 'http://localhost:8094';
const orders = __ENV.ORDERS || 'http://localhost:8095';
const op = __ENV.OP || 'dashboard';

http.setResponseCallback(http.expectedStatuses(200, 201, 204));

export const options = {
    scenarios: {
        load: {
            executor: 'constant-vus',
            vus: parseInt(__ENV.VUS || '5'),
            duration: __ENV.DUR || '20s',
        },
    },
};

export function setup() {
    if (op !== 'orders-write') return {};
    const payload = JSON.stringify({
        name: `k6 load ${Date.now()}`,
        category: 'LoadTest',
        price: 1,
        stock: 999999,
        isActive: true,
    });
    const res = http.post(`${catalog}/api/products`, payload, {
        headers: { 'Content-Type': 'application/json' },
    });
    if (res.status !== 201) {
        throw new Error(`load product create failed: ${res.status}`);
    }
    return { productId: res.json() };
}

export function teardown(data) {
    if (data && data.productId) {
        http.del(`${catalog}/api/products/${data.productId}`);
    }
}

export default function (data) {
    if (op === 'dashboard') {
        const r = http.get(`${front}/`);
        check(r, {
            'dashboard 200': (r) => r.status === 200,
            'dashboard not degraded': (r) => (r.body || '').indexOf('is unavailable right now') === -1,
        });
    } else if (op === 'products-page') {
        const r = http.get(`${front}/Pages/Products/`);
        check(r, { 'products page 200': (r) => r.status === 200 });
    } else if (op === 'catalog-read') {
        const r = http.get(`${catalog}/api/products?includeDeleted=false`);
        check(r, { 'catalog list 200': (r) => r.status === 200 });
    } else if (op === 'orders-read') {
        const r = http.get(`${orders}/api/orders?includeDeleted=false&skip=0&take=20`);
        check(r, { 'orders list 200': (r) => r.status === 200 });
    } else if (op === 'orders-write') {
        const body = JSON.stringify({
            customerName: `k6 ${__VU}-${__ITER}`,
            customerEmail: 'k6@load.local',
            orderDate: new Date().toISOString(),
            deliveryDate: new Date(Date.now() + 3 * 86400000).toISOString(),
            status: 'Pending',
            priority: 'Normal',
            extras: [],
            items: [{
                productId: data.productId,
                productName: 'k6 load',
                quantity: 1,
                unitPrice: 1,
            }],
        });
        const c = http.post(`${orders}/api/orders`, body, {
            headers: { 'Content-Type': 'application/json' },
        });
        check(c, { 'order created 201': (r) => r.status === 201 });
        if (c.status === 201) {
            const d = http.del(`${orders}/api/orders/${c.json()}`);
            check(d, { 'order deleted 204': (r) => r.status === 204 });
        }
    }
}

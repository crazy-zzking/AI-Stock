// ============================================================
// API 响应包装类型
// ============================================================

import type { AxiosResponse } from 'axios';

/** 标准 API 响应 — 列表接口 */
export type ApiListResponse<T> = AxiosResponse<T[]>;

/** 标准 API 响应 — 单对象接口 */
export type ApiResponse<T> = AxiosResponse<T>;

/** 标准 API 响应 — 无数据 */
export type ApiEmptyResponse = AxiosResponse<void>;

# ---- 构建阶段：编译 React 静态资源 ----
FROM node:20-alpine AS build
WORKDIR /app

# 用 npmmirror 加速依赖安装（国内服务器）
RUN npm config set registry https://registry.npmmirror.com

COPY package.json package-lock.json* ./
RUN npm install --no-audit --no-fund

COPY . .

# 前端通过相对路径 /api 调后端，由 nginx 反代到 api 容器；
# 限制 node 堆内存，避免 1.8G 小机构建 OOM。
ENV REACT_APP_API_BASE_URL=/api \
    GENERATE_SOURCEMAP=false \
    CI=false \
    NODE_OPTIONS=--max-old-space-size=1536
RUN npm run build

# ---- 运行阶段：nginx 托管 ----
FROM nginx:1.27-alpine AS final
COPY --from=build /app/build /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80

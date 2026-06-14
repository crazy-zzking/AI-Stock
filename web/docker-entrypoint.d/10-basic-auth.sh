#!/bin/sh
# nginx 启动前执行：按环境变量生成 HTTP Basic Auth 凭证。
#   BASIC_AUTH_USER     默认 admin
#   BASIC_AUTH_PASSWORD 为空则不启用密码（整站放行）
# 生成：/etc/nginx/.htpasswd（密码哈希）+ /etc/nginx/auth_basic.inc（auth_basic 指令）
set -e

INC=/etc/nginx/auth_basic.inc
HTPASSWD=/etc/nginx/.htpasswd
USER="${BASIC_AUTH_USER:-admin}"

if [ -n "$BASIC_AUTH_PASSWORD" ]; then
    htpasswd -bcB "$HTPASSWD" "$USER" "$BASIC_AUTH_PASSWORD" >/dev/null 2>&1
    cat > "$INC" <<EOF
auth_basic "AI-Stock 受限访问";
auth_basic_user_file $HTPASSWD;
EOF
    echo "[basic-auth] 已启用，用户=$USER"
else
    # 未配置密码：写空 include，整站放行（避免 auth_basic_user_file 缺失导致 500）
    : > "$INC"
    echo "[basic-auth] 未配置 BASIC_AUTH_PASSWORD，整站放行"
fi

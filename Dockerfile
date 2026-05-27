# 使用官方 .NET 9 SDK 镜像作为构建阶段
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# 复制项目文件
COPY ["AIStock.Web/AIStock.Web.csproj", "AIStock.Web/"]
COPY ["AIStock.Core/AIStock.Core.csproj", "AIStock.Core/"]
COPY ["AIStock.Infrastructure/AIStock.Infrastructure.csproj", "AIStock.Infrastructure/"]
COPY ["AIStock.Data/AIStock.Data.csproj", "AIStock.Data/"]
COPY ["AIStock.LLM/AIStock.LLM.csproj", "AIStock.LLM/"]
COPY ["AIStock.Intelligence/AIStock.Intelligence.csproj", "AIStock.Intelligence/"]
COPY ["AIStock.EventEngine/AIStock.EventEngine.csproj", "AIStock.EventEngine/"]
COPY ["AIStock.Knowledge/AIStock.Knowledge.csproj", "AIStock.Knowledge/"]
COPY ["AIStock.Feature/AIStock.Feature.csproj", "AIStock.Feature/"]
COPY ["AIStock.Strategy/AIStock.Strategy.csproj", "AIStock.Strategy/"]
COPY ["AIStock.Risk/AIStock.Risk.csproj", "AIStock.Risk/"]
COPY ["AIStock.Execution/AIStock.Execution.csproj", "AIStock.Execution/"]
COPY ["AIStock.Orchestrator/AIStock.Orchestrator.csproj", "AIStock.Orchestrator/"]
COPY ["AIStock.Monitor/AIStock.Monitor.csproj", "AIStock.Monitor/"]
COPY ["AIStock.Worker/AIStock.Worker.csproj", "AIStock.Worker/"]

# 还原依赖
RUN dotnet restore "AIStock.Web/AIStock.Web.csproj"

# 复制所有源代码
COPY . .

# 构建项目
WORKDIR "/src/AIStock.Web"
RUN dotnet build -c Release -o /app/build

# 发布阶段
FROM build AS publish
RUN dotnet publish -c Release -o /app/publish

# 运行阶段 - 使用 mcr.microsoft.com/dotnet/aspnet:9.0
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# 安装 Playwright 依赖（Chromium 需要的系统库）
RUN apt-get update && apt-get install -y \
    fonts-wqy-zenhei \
    fonts-noto-color-emoji \
    libatk1.0-0 \
    libatk-bridge2.0-0 \
    libcups2 \
    libdrm2 \
    libxkbcommon0 \
    libxcomposite1 \
    libxdamage1 \
    libxfixes3 \
    libxrandr2 \
    libgbm1 \
    libpango-1.0-0 \
    libcairo2 \
    libasound2 \
    libnspr4 \
    libnss3 \
    libxshmfence1 \
    wget \
    ca-certificates \
    && rm -rf /var/lib/apt/lists/*

# 安装 Playwright CLI 并下载浏览器
RUN dotnet tool install --global Microsoft.Playwright.CLI
ENV PATH="${PATH}:/root/.dotnet/tools"

# 复制发布文件
COPY --from=publish /app/publish .

# 创建日志目录
RUN mkdir -p /app/logs

# 暴露端口
EXPOSE 8080

# 设置环境变量
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright

# 安装 Playwright 浏览器
RUN playwright install chromium

# 启动应用
ENTRYPOINT ["dotnet", "AIStock.Web.dll"]

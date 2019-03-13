cd /d %~dp0
docker build -t "repository.xiaobao100.com/docker/library/exceptionless:5.0.6-dev" .
docker push repository.xiaobao100.com/docker/library/exceptionless:5.0.6-dev
pause
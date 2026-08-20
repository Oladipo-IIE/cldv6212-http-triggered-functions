REM ============================================
REM 0. Remove previous containers and the network
REM Set connectionstring
REM ============================================

docker rm -f user-api 
docker rm -f azurite
docker network rm my-network

set "CONN_STRING=AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;AccountName=devstoreaccount1;BlobEndpoint=http://azurite:10000/devstoreaccount1;QueueEndpoint=http://azurite:10001/devstoreaccount1;TableEndpoint=http://azurite:10002/devstoreaccount1;"
REM ============================================
REM 1. Build the application Docker image
REM Run from the solution directory
REM ============================================

docker build -t user-api-image:latest .

cd
REM ============================================
REM 2. Create a Docker network
REM ============================================

docker network create my-network


REM ============================================
REM 3. Run Azurite and connect it to the network
REM ============================================

docker run -d ^
  --name azurite ^
  --network my-network ^
  -p 10000:10000 ^
  -p 10001:10001 ^
  -p 10002:10002 ^
  mcr.microsoft.com/azure-storage/azurite


REM ============================================
REM 4. Run the application on the same network
REM ============================================

docker run -d ^
  --name user-api ^
  --network my-network ^
  -p 8080:80 ^
  -e AzureWebJobsStorage="%CONN_STRING%" ^
  user-api-image


REM ============================================
REM 5. Check that both containers are running
REM ============================================

docker ps


REM ============================================
REM 6. Check that both are on my-network
REM ============================================

docker network inspect my-network
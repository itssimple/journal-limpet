docker run --rm --name journal-limpet-db -e POSTGRES_PASSWORD=local-docker-instance -d -p 5432:5432 -v D:/docker-things/postgres-data:/var/lib/postgresql/data postgres
docker exec -it journal-limpet-db createuser -U postgres journal-limpet-db journal-limpet
docker exec -it journal-limpet-db createdb -U postgres -O journal-limpet-db journal-limpet
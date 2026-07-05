up:
	docker-compose -f docker-compose.yml up -d

down:
	docker-compose -f docker-compose.yml down

reset:
	docker-compose -f docker-compose.yml down -v
	docker-compose -f docker-compose.yml up -d

seed:
	docker exec -i quiz-db psql -U app -d quiz_db < quiz_data.sql

full:
	make reset
	make seed
	make run